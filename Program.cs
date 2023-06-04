using System;
using System.Net;
using System.Text;
using System.Management;
using Microsoft.Win32;
using Amazon.S3;
using Amazon.S3.Transfer;
using System.IO;
using System.Linq;

namespace s3uploader
{
    class Program
    {
        static void Main(string[] args)
        {
            var bucketName = System.Environment.GetEnvironmentVariable("S3_BUCKET_NAME");

            if (string.IsNullOrEmpty(bucketName))
            {
                bucketName = "iress-market-content-team-log-bucket"; // default log bucket
            }

            var accessKeyId = System.Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
            var secretAccessKey = System.Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
            var sessionToken = System.Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN");

            try
            {
                var awsCredentials = new Amazon.Runtime.SessionAWSCredentials(accessKeyId, secretAccessKey, sessionToken);
                var transferUtility = new TransferUtility(new AmazonS3Client(awsCredentials, Amazon.RegionEndpoint.APSoutheast2));

                ManagementClass mngmtClass = new ManagementClass("Win32_Process");
                foreach (ManagementObject o in mngmtClass.GetInstances())
                {
                    var processName = o["Name"].ToString();

                    if (processName.ToLower().Contains("reader"))
                    {
                        var application = processName.Replace(".exe", "");
                        var instance = "";

                        var commandline = o["CommandLine"];
                        if (commandline == null)
                        {
                            Console.WriteLine($"Unable to get command line for {processName}. ");
                            continue;
                        }
                        var commandlineStr = commandline.ToString();

                        var instanceStart = commandlineStr.IndexOf("-r ");
                        if (instanceStart >= 0)
                        {
                            instance = commandlineStr.Substring(instanceStart + 3);
                            var instanceEnd = instance.IndexOf(" ");
                            if (instanceEnd >= 0)
                            {
                                instance = instance.Substring(0, instanceEnd);
                            }
                        }
                        else 
                        {
                            Console.WriteLine("Info: No instance is specified.");
                        }

                        var path = Registry.GetValue("HKEY_CURRENT_USER\\SOFTWARE\\DFS\\" + application + instance + "\\BroadcastLogging\\ExtFeedProc", "Path", "").ToString();
                        var prefix = Registry.GetValue("HKEY_CURRENT_USER\\SOFTWARE\\DFS\\" + application + instance + "\\BroadcastLogging\\ExtFeedProc", "PermanentPrefix", "").ToString();

                        string[] logs = Directory.GetFiles(path, (prefix == "" ? application : prefix) + "*.log");

                        foreach (string log in logs)
                        {
                            string fileName = log.Substring(log.LastIndexOf('\\') + 1);

                            string server = Dns.GetHostName();

                            string date = GetDateFromLog(log);

                            if (date != "")
                            {
                                if (string.IsNullOrEmpty(instance))
                                {
                                    instance = "DEFAULT"; // default instance name as frontend need a instance name
                                }
                                string key = application + "/" + server + "/" + date + "/" + instance + "/" + fileName;

                                Console.WriteLine("key is: "+ key);
                                Console.WriteLine("log is: " + log);
                                Console.WriteLine("bucketName is: " + bucketName);

                                TransferUtilityUploadRequest uR = new TransferUtilityUploadRequest
                                {
                                    BucketName = bucketName,
                                    FilePath = log,
                                    CannedACL = S3CannedACL.Private,
                                    Key = key,
                                    ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS,
                                };

                                uR.Metadata.Add("application", application);
                                uR.Metadata.Add("server", server);
                                uR.Metadata.Add("instance", instance);
                                uR.Metadata.Add("date", date);

                                transferUtility.Upload(uR);
                                Console.WriteLine(log + " Uploaded");
                            }
                        }
                    }
                }

                Console.WriteLine("Done!");
            }
            catch (AmazonS3Exception e)
            {
                Console.WriteLine(e.Message, e.InnerException);
            }
        }

        static string GetDateFromLog(string file)
        {
            byte[] buffer = new byte[10];

            try
            {
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                {
                    var bytes_read = fs.Read(buffer, 0, buffer.Length);

                    if (bytes_read == buffer.Length && buffer.SequenceEqual<byte>(Encoding.Default.GetBytes("Version1.3")))
                    {
                        buffer = new byte[4];
                        bytes_read = fs.Read(buffer, 0, buffer.Length);
                        if (bytes_read == buffer.Length)
                        {
                            UInt32 date = BitConverter.ToUInt32(buffer, 0);
                            return date.ToString();
                        }
                    }
                }
            }
            catch (System.UnauthorizedAccessException)
            {
            }
            catch (System.IO.IOException)
            {
            }

            return "";
        }
    }
}
