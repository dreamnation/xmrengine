using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Reflection;
using System.Security.Cryptography;

using MySql.Data.MySqlClient;

namespace Careminster
{
	public class Program
	{
		static int count = 0;
		static string dest;

		static SHA1CryptoServiceProvider SHA1 = new SHA1CryptoServiceProvider();

		public static int Main(string[] args)
		{
			dest = args[1];

			ProcessDir(args[0]);

			return 0;
		}

		public static void ProcessDir(string d)
		{
			string[] dirs = Directory.GetDirectories(d);
			string[] files = Directory.GetFiles(d);

			foreach (string file in files)
			{
				string f = Path.GetFileName(file);

				if (f.EndsWith(".meta"))
					continue;
				if (!f.StartsWith("blk_"))
					continue;
				
				count++;

				if ((count % 100) == 0)
					Console.WriteLine("{0} assets processed", count);

				string destfile = Path.Combine(dest, GetDestination(file));

//				Console.WriteLine("{0} = {1}", file, destfile);

				Directory.CreateDirectory(Path.GetDirectoryName(destfile));
				try
				{
					File.Move(file, destfile);
				}
				catch(System.IO.IOException e)
				{
					if (e.Message.StartsWith("Win32 IO returned ERROR_ALREADY_EXISTS"))
						File.Delete(file);
					else
						throw;
				}
			}

			foreach (string dir in dirs)
			{
				ProcessDir(dir);
			}
		}

		public static string GetDestination(string filename)
		{
			byte[] data = File.ReadAllBytes(filename);

			byte[] hash = SHA1.ComputeHash(data);

			string hash_str = BitConverter.ToString(hash).Replace("-", String.Empty);

			string dir =  Path.Combine(hash_str.Substring(0, 2),
								Path.Combine(hash_str.Substring(2, 2),
								Path.Combine(hash_str.Substring(4, 2),
								hash_str.Substring(6, 4))));

			string file = Path.Combine(dir, hash_str);

			return file;
		}
	}
}
