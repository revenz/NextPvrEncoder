using System;
using System.IO;
using System.Configuration;
using System.Diagnostics;
using System.Data.SqlClient;
using System.Linq;

namespace NextPvrEncoder
{
	class MainClass
	{
        static string EncoderFile, EncoderParameters, OutputType, ConnectionString, DbType, SmbPathReplace, SmbPathReplacement;

		public static void Main (string[] args)
		{	
			EncoderFile = ConfigurationManager.AppSettings["encoderFile"];
			EncoderParameters = ConfigurationManager.AppSettings["encoderParameters"];
			OutputType = ConfigurationManager.AppSettings["outputType"];
			ConnectionString = ConfigurationManager.AppSettings["connstr"];
			DbType = ConfigurationManager.AppSettings["dbtype"];
			SmbPathReplace = ConfigurationManager.AppSettings["smbpathreplace"] ?? "";
			SmbPathReplacement = ConfigurationManager.AppSettings["smbpathreplacement"] ?? "";
			
			if(String.IsNullOrEmpty(EncoderFile) || String.IsNullOrEmpty(EncoderParameters) || String.IsNullOrEmpty(OutputType) || String.IsNullOrEmpty(ConnectionString))
			{
				Console.WriteLine("Please check your configuration file.");
				return;
			}
			
            if(args.Length == 0)
				return;
			string inputfile = args[0];
			if(!System.IO.File.Exists(inputfile))
				return;

            AppendToQueue(inputfile);

            // check to see if another transcoder is running
            var currentProcess = Process.GetCurrentProcess();
            foreach (Process p in Process.GetProcessesByName(currentProcess.ProcessName))
            {
                if (p.Id != currentProcess.Id)
                {
                    // another one is running
                    Console.WriteLine("Another RecordingsTranscoder running, exiting, item added to queue for later processing");
                    return;
                }
            }

            ProcessQueue();
		}

        static void ProcessQueue()
        {
            string queueItem = GetNextQueuedItem();
            while (queueItem != null)
            {
                ProcessFile(queueItem);

                queueItem = GetNextQueuedItem();
            }
        }

        static void ProcessFile(string Filename)
        {
            Console.WriteLine("Processing File: " + Filename);

            if (!File.Exists(Filename))
                return;

            string inputfile = Filename;

			string outputfile = inputfile.Substring(0, inputfile.LastIndexOf(".")+1) + OutputType;
			
			using(Process process = new Process())
			{
				process.StartInfo.FileName = EncoderFile;
				process.StartInfo.Arguments = String.Format(EncoderParameters, inputfile, outputfile);
				
				Console.WriteLine("Encoder File: " + process.StartInfo.FileName);
				Console.WriteLine("Encoder Parameters: " + process.StartInfo.Arguments);
				
				process.StartInfo.UseShellExecute = false;
				process.Start();
				process.WaitForExit();
			}
			
			// test output file was created
			if(!System.IO.File.Exists(outputfile))
			{
				Console.WriteLine("Failed to create output file.");
				return;
			}
			
			// check file size, should update this to be better, but meh
			if(new FileInfo(outputfile).Length < new FileInfo(inputfile).Length * .2)  // assume it has to be at least 20% size of the original file...
			{
				Console.WriteLine("Output file size is too small, assuming transcode failed");
				DeleteFile(outputfile);
				return;
			}

			string olddbname = inputfile;
			string newdbname = outputfile;
			
			if(inputfile.ToLower().StartsWith(SmbPathReplace.ToLower()))
			{
				olddbname = SmbPathReplacement + inputfile.Substring(SmbPathReplace.Length);
				newdbname = SmbPathReplacement + outputfile.Substring(SmbPathReplace.Length);
			}

            UpdateSqlLite(ConnectionString, olddbname, newdbname);
			
			// delete original file
            DeleteFile(inputfile);
        }


        static string QueueFile = "transcoderqueue.txt";
        static void AppendToQueue(string Filename)
        {
            System.IO.File.AppendAllText(QueueFile, Filename + Environment.NewLine); 
        }

        static string GetNextQueuedItem()
        {
            if (!System.IO.File.Exists(QueueFile))
                return null;

            var lines = File.ReadLines(QueueFile).ToArray();
            if (lines.Length > 1)
                File.WriteAllLines(QueueFile, lines.Skip(1));
            else
                File.Delete(QueueFile);
            return lines[0];
        }
		
		static bool DeleteFile(string Filename)
		{
			try
			{
				File.Delete(Filename);
				return !File.Exists(Filename);
			}
			catch(Exception)
			{
				return false;
			}
		}
		/*
		static void UpdateMySql(string connstr, string inputfile, string outputfile)
		{
			Console.WriteLine("Updating mysql database.");
			using(MySql.Data.MySqlClient.MySqlConnection conn = new MySql.Data.MySqlClient.MySqlConnection(connstr))
			{
				conn.Open();
				
				string cmdtext = "update Recording set RecordingFileName = @NewFilename where RecordingFileName = @OldFilename;";
				using(MySql.Data.MySqlClient.MySqlCommand cmd = new MySql.Data.MySqlClient.MySqlCommand(cmdtext, conn))
				{
					cmd.Parameters.AddWithValue("@NewFilename", outputfile);
					cmd.Parameters.AddWithValue("@OldFilename", inputfile);
					int rows = cmd.ExecuteNonQuery();
					if(rows > 0)
						Console.WriteLine("Successfully updated database.");
					else
						Console.WriteLine("Failed to update database.");
				}
				
				conn.Close();
			}
		}
		
		static void UpdateSqlServer(string connstr, string inputfile, string outputfile)
		{
			Console.WriteLine("Updating SQL Server database.");
			using(SqlConnection conn = new SqlConnection(connstr))
			{
				conn.Open();
				string cmdtext = "update Recording set RecordingFileName = @NewFilename where RecordingFileName=@OldFilename";
				using(SqlCommand cmd = new SqlCommand(cmdtext, conn))
				{
					cmd.Parameters.AddWithValue("@NewFilename", outputfile);
					cmd.Parameters.AddWithValue("@OldFilename", inputfile);
					int rows = cmd.ExecuteNonQuery();
					if(rows > 0)
						Console.WriteLine("Successfully updated database.");
					else
						Console.WriteLine("Failed to update database.");
				}
				conn.Close();
			}
			
		}
        */
        static void UpdateSqlLite(string connstr, string inputfile, string outputfile)
        {
            Console.WriteLine("Update Sql Lite database.");
            using (System.Data.SQLite.SQLiteConnection conn = new System.Data.SQLite.SQLiteConnection(connstr))
            {
                conn.Open();
                string cmdtext = "update scheduled_recording set filename = @NewFilename where lower(filename) = @OldFilename";
                using (System.Data.SQLite.SQLiteCommand cmd = new System.Data.SQLite.SQLiteCommand(cmdtext, conn))
                {
                    cmd.Parameters.AddWithValue("@NewFilename", outputfile);
                    cmd.Parameters.AddWithValue("@OldFilename", inputfile.ToLower());
                    int rows = cmd.ExecuteNonQuery();
                    if (rows > 0)
                        Console.WriteLine("Sucessfully updated database.");
                    else
                        Console.WriteLine("Failed to update database.");
                }
                conn.Close();
            }
        }
		
	}
}
