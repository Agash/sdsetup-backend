using ICSharpCode.SharpZipLib.Zip;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;


namespace sdsetup_backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class v1 : ControllerBase
    {

        [HttpGet("fetch/zip")]
        public ActionResult FetchZip([FromHeader] FetchZipInputModel model)
        {
            if (!ModelState.IsValid)
                return StatusCode(400, "Invalid input");
            
            if (Program.uuidLocks.Contains(model.UUID))
                return StatusCode(400, "UUID " + model.UUID + " locked");

            if (!Program.validChannels.Contains(model.Channel))
                return StatusCode(400, "Invalid channel");

            if (!Directory.Exists(Program.Files + "/" + model.PackageSet))
                return StatusCode(400, "Invalid packageset");

            if (System.IO.File.Exists(Program.Files + "/" + model.PackageSet + "/.PRIVILEGED.FLAG") && !Program.IsUuidPriveleged(model.UUID))
                return StatusCode(401, "You do not have access to that packageset");

            string tempdir = Program.Temp + "/" + model.UUID;
            try
            {
                Program.uuidLocks.Add(model.UUID);

                string[] requestedPackages = model.Packages.Split(';');
                List<KeyValuePair<string, string>> files = new List<KeyValuePair<string, string>>();
                foreach (string k in requestedPackages)
                {
                    //sanitize input
                    if (k.Contains("/") || k.Contains("/") || k.Contains("..") || k.Contains("~") || k.Contains("%"))
                    {
                        Program.uuidLocks.Remove(model.UUID);
                        return StatusCode(400, "hackerman");
                    }

                    if (Directory.Exists(Program.Files + "/" + model.PackageSet + "/" + k + "/" + model.Channel))
                    {
                        foreach (string f in EnumerateAllFiles(Program.Files + "/" + model.PackageSet + "/" + k + "/" + model.Channel))
                        {
                            if (model.Client == "hbswitch")
                            {
                                if (f.StartsWith(Program.Files + "/" + model.PackageSet + "/" + k + "/" + model.Channel + "/sd"))
                                {
                                    files.Add(new KeyValuePair<string, string>(f.Replace(Program.Files + "/" + model.PackageSet + "/" + k + "/" + model.Channel + "/sd", ""), f));
                                }
                            }
                            else
                            {
                                files.Add(new KeyValuePair<string, string>(f.Replace(Program.Files + "/" + model.PackageSet + "/" + k + "/" + model.Channel, ""), f));
                            }
                        }
                    }
                }

                DeletingFileStream stream = (DeletingFileStream)ZipFromFilestreams(files.ToArray(), model.UUID);

                Program.generatedZips[model.UUID] = stream;
                stream.Timeout(30000);

                Program.uuidLocks.Remove(model.UUID);
                return Ok("READY");
            }
            catch (Exception)
            {
                Program.uuidLocks.Remove(model.UUID);
                return StatusCode(500, "Internal server error occurred");
            }

        }

        [HttpGet("fetch/generatedzip/{uuid}")]
        public ActionResult FetchGeneratedZip(string uuid)
        {
            try
            {
                if (Program.generatedZips.ContainsKey(uuid))
                {
                    Program.generatedZips[uuid].StopTimeout();
                    DeletingFileStream stream = Program.generatedZips[uuid];
                    Program.generatedZips[uuid] = null;
                    Program.generatedZips.Remove(uuid);
                    if (stream == null)
                    {
                        //StatusCode 410 Gone: The requested resource is no longer available at the server and no forwarding address is known. This condition is expected to be considered permanent (since UUID..).
                        return StatusCode(410, "Expired");
                    }
                    string zipname = ("SDSetup(" + DateTime.Now.ToShortDateString() + ").zip").Replace("-", ".").Replace("_", ".");
                    Response.Headers["Content-Disposition"] = "filename=" + zipname;
                    return new FileStreamResult(stream, "application/zip");
                }
                else
                {
                    return StatusCode(410, "Expired");
                }
            }
            catch (Exception)
            {
                Program.generatedZips[uuid] = null;
                return StatusCode(410, "Expired");
            }

        }

        [HttpGet("fetch/manifest/{uuid}/{packageset}")]
        public ActionResult FetchManifest(string uuid, string packageset)
        {
            if (!Directory.Exists(Program.Files + "/" + packageset))
            {
                return Ok(packageset);
            }
            else if (System.IO.File.Exists(Program.Files + "/" + packageset + "/.PRIVILEGED.FLAG") && !Program.IsUuidPriveleged(uuid))
            {
                return StatusCode(401, "You do not have access to that packageset");
            }

            return Ok(Program.Manifests[packageset]);
        }

        [HttpGet("get/latestpackageset")]
        public ActionResult GetLatestPackageset()
        {
            return Ok(Program.latestPackageset);
        }

        [HttpGet("get/latestappversion/switch")]
        public ActionResult GetLatestAppVersion()
        {
            return Ok(Program.latestAppVersion);
        }

        [HttpGet("get/latestappdownload/switch")]
        public ActionResult GetLatestAppDownload()
        {
            string zipname = "sdsetup-switch.nro";
            Response.Headers["Content-Disposition"] = "filename=" + zipname;
            return new FileStreamResult(new FileStream(Program.Config + "/sdsetup-switch.nro", FileMode.Open, FileAccess.Read, FileShare.ReadWrite), "application/octet-stream");
        }

        [HttpGet("set/latestpackageset/{uuid}/{packageset}")]
        public ActionResult SetLatestPackageset(string uuid, string packageset)
        {
            if (!Program.IsUuidPriveleged(uuid))
                return StatusCode(401, "UUID not priveleged");

            Program.latestPackageset = packageset;
            return Ok("Success");
        }

        [HttpGet("admin/reloadall/{uuid}")]
        public ActionResult ReloadEverything(string uuid)
        {
            if (!Program.IsUuidPriveleged(uuid))
                return StatusCode(401, "UUID not priveleged");

            return Ok(Program.ReloadEverything());
        }

        [HttpGet("admin/overrideprivelegeduuid/")]
        public ActionResult OverridePrivelegedUuid(string uuid)
        {
            if (Program.OverridePrivelegedUuid())
                return Ok("Success");

            return StatusCode(400, "Failed");
        }

        [HttpGet("admin/checkuuidstatus/{uuid}")]
        public ActionResult CheckUuidStatus(string uuid)
        {
            if (Program.IsUuidPriveleged(uuid))
                return Ok("UUID is priveleged");

            return StatusCode(401, "UUID not priveleged");
        }

        [HttpGet("admin/setprivelegeduuid/{oldUuid}/{newUuid}")]
        public ActionResult SetPrivelegedUuid(string oldUuid, string newUuid)
        {
            if (Program.SetPrivelegedUUID(oldUuid, newUuid))
                return Ok("Success");

            return StatusCode(400, "Old UUID invalid");
        }


        public static Stream ZipFromFilestreams(KeyValuePair<string, string>[] files, string uuid)
        {

            DeletingFileStream outputMemStream = new DeletingFileStream(Program.Temp + "/" + Guid.NewGuid().ToString().Replace("-", "").ToLower(), FileMode.Create, uuid);
            ZipOutputStream zipStream = new ZipOutputStream(outputMemStream);

            zipStream.SetLevel(3); //0-9, 9 being the highest level of compression

            foreach (KeyValuePair<string, string> f in files)
            {
                ZipEntry newEntry = new ZipEntry(f.Key);
                newEntry.DateTime = DateTime.Now;
                zipStream.PutNextEntry(newEntry);
                FileStream fs = new FileStream(f.Value, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.CopyTo(zipStream, 4096);
                //StreamUtils.Copy(fs, zipStream, new byte[4096]);
                fs.Close();
                zipStream.CloseEntry();
            }


            zipStream.IsStreamOwner = false;    // False stops the Close also Closing the underlying stream.
            zipStream.Close();          // Must finish the ZipOutputStream before using outputMemStream.

            outputMemStream.Position = 0;

            return outputMemStream;
        }

        private static string[] EnumerateAllFiles(string dir)
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToArray();
        }


        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs, bool overwriteFiles)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, overwriteFiles);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, temppath, copySubDirs, overwriteFiles);
                }
            }
        }

        public class FetchZipInputModel
        {
            [FromHeader(Name = "SDSETUP-UUID")]
            [Required]
            public string UUID { get; set; }

            [FromHeader(Name = "SDSETUP-PACKAGESET")]
            [Required]
            public string PackageSet { get; set; }

            [FromHeader(Name = "SDSETUP-CHANNEL")]
            [Required]
            public string Channel { get; set; }

            [FromHeader(Name = "SDSETUP-PACKAGES")]
            [Required]
            public string Packages { get; set; }

            [FromHeader(Name = "SDSETUP-CLIENT")]
            public string Client { get; set; }
        }
    }
}
