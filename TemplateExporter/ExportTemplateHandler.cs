﻿using System;
using System.Text;
using MonoDevelop.Components.Commands;
using MonoDevelop.Projects;
using MonoDevelop.Ide;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Xml;


namespace TemplateExporter
{
	class ExportTemplateHandler:CommandHandler
	{
		Project proj;

		protected override void Run ()
		{
			try {			
			
				var files = proj.Files;

				if (!files.Any ())
					return;
				
				var rootDir = files [0].FilePath.ParentDirectory;
				var projectTemplate = "ProjectTemplate";
				var templateDir = Path.Combine (rootDir, projectTemplate);

				// Cleanup: Delete template directory if it exists
				if(Directory.Exists(templateDir))
					Directory.Delete (templateDir, true);

				// Create template directory
				Directory.CreateDirectory (templateDir);

				files = proj.Files;
				StringBuilder runtimeXml = new StringBuilder("\t\n\t<Runtime>\n\t\t<Import file=\"ProjectTemplate.xpt.xml\" />");
				StringBuilder filesXml = new StringBuilder("\n\t\t\t<Files>");
				string packages = "";

				int i = -1;
				foreach (var file in files) 
				{
					if(file.FilePath.ToString().ToLower().EndsWith("packages.config"))
					{
						// Get packages from packages.config
						packages = GetPackages(file);
						continue;
					}

					i++;
					// Exclude files from template
					if(file.FilePath.ToString().ToLower().Contains("/bin/") || 
						file.FilePath.ToString().ToLower().Contains("/projecttemplate/") ||
						file.FilePath.ToString().ToLower().Contains("/packages/") ||
						file.FilePath.ToString().ToLower().EndsWith(".xpt.xml") ||
						file.FilePath.ToString().ToLower().EndsWith(".addin.xml") ||
						file.FilePath.Extension == string.Empty)
					{	
						Console.WriteLine("{0} Export Template SKIP: {1}", i, file.FilePath);
						continue;
					}

					// Copy or Create files from project to ProjectTemplate directory
					var dir = Path.GetDirectoryName(file.FilePath).Replace(rootDir, Path.Combine(rootDir, projectTemplate));
					Directory.CreateDirectory (dir);
					var templateFilePath = Path.Combine(dir, file.ProjectVirtualPath.FileName);
					Console.WriteLine("{0} Export Template Copy: {1}", i, file.FilePath);

					if(file.ProjectVirtualPath.Extension.ToLower() == ".png")
					{
						// copy file
						File.Copy(file.FilePath, templateFilePath, true);
					}
					else
					{
						// create file, so we can replace namespaces					
						var content = File.ReadAllText(file.FilePath);
						content = content.Replace(proj.Name, "${Namespace}");
						CreateFile(templateFilePath, content, true);
					}

					runtimeXml.Append(string.Format("\n\t\t<Import file=\"{0}\" />", file.ProjectVirtualPath));
					AppendFile(ref filesXml, file);
				}

				runtimeXml.Append("\n\t</Runtime>");
				filesXml.Append("\n\t\t\t</Files>");

				// Set template xml file paths
				var xptFile = Path.Combine(rootDir, "ProjectTemplate.xpt.xml");
				var addinFile = Path.Combine(rootDir, proj.Name + ".addin.xml");

				// Creates the template files if not exists
				CreateProjectFile (xptFile, Constants.XptXmlAndroid);
				CreateProjectFile (addinFile, Constants.AddInXmlDroid);

				// get xml from template files
				var xptXml = File.ReadAllText(xptFile);
				var addInXml = File.ReadAllText(addinFile);

				var version = GetVersion(addInXml);
				xptXml = xptXml.Replace("[VERSION]", string.Format ("v{0}", version));
				xptXml = xptXml.Replace("[FILES_PLACEHOLDER]", filesXml.ToString());
				xptXml = xptXml.Replace("[PACKAGES_PLACEHOLDER]", packages);

				//write template files
				File.WriteAllText (xptFile.Replace(rootDir, Path.Combine(rootDir, projectTemplate)), xptXml);				
				File.WriteAllText (addinFile.Replace(rootDir, Path.Combine(rootDir, projectTemplate)), addInXml.Replace("[RUNTIME_PLACEHOLDER]", runtimeXml.ToString()));

				// create .mpack
				if(!RunMDTool(templateDir, "-v setup pack Randstad.Template.Droid.addin.xml"))
				{
					Console.WriteLine("Export Template ERROR: Unable to generate .mpack");
					return;
				}

				var mpack = string.Format("MonoDevelop.Randstad.Template.Droid_{0}.mpack", version);
				File.Move(Path.Combine(templateDir, mpack), Path.Combine(rootDir, mpack));

				// install .mpack: INSTALL NOT WORKING...
//				if(!RunMDTool(rootDir, string.Format("-v setup install -y {0}", mpack)))
//				{
//					Console.WriteLine("Export Template ERROR: Unable to install .mpack");
//					return;
//				}

				Console.WriteLine("Export Template SUCCESS.");
			} 
			catch (Exception ex) 
			{
				// Log exception
				Console.WriteLine ("Export Template EXCEPTION: {0}", ex.Message);
			}
		}

		protected override void Update (CommandInfo info)
		{
			proj = IdeApp.ProjectOperations.CurrentSelectedProject;		
			info.Enabled = proj != null;
		}

		private void AppendFile(ref StringBuilder filesXml, ProjectFile file)
		{
			var path = file.ProjectVirtualPath.ToString().Replace(file.ProjectVirtualPath.FileName, "");
			var subDirs = path.Split('/').ToList();
			subDirs.RemoveAll (x => x == "");

			foreach (var dir in subDirs) {
				// add directory node
				filesXml.Append(string.Format("\n\t\t\t\t<Directory name=\"{0}\">", dir));
			}

			// append file
			if(IsRawFile(file))
				filesXml.Append(string.Format("\n\t\t\t\t<RawFile name=\"{0}\" src=\"{1}\" />", file.ProjectVirtualPath.FileName, file.ProjectVirtualPath));
			else
				filesXml.Append(string.Format("\n\t\t\t\t<File name=\"{0}\" src=\"{1}\" />", file.ProjectVirtualPath.FileName, file.ProjectVirtualPath));

			for (int i = 0; i < subDirs.Count(); i++) {
				// close directory node
				filesXml.Append("\n\t\t\t\t</Directory>");
			}
		}

		string GetPackages(ProjectFile file)
		{
			var packages = File.ReadAllText(file.FilePath);
			packages = packages.Replace ("<?xml version=\"1.0\" encoding=\"utf-8\"?>", "");
			packages = packages.Replace ("<packages>", "");
			packages = packages.Replace ("</packages>", "");

			return packages.Trim();
		}

		private bool IsRawFile(ProjectFile file)
		{
			return file.ProjectVirtualPath.Extension.ToLower () == ".png"
				|| file.ProjectVirtualPath.Extension.ToLower () == ".txt";
		}

		private string GetVersion(string addInXml)
		{			
			// gets the version number from addInXml
			var v = "version=\"";
			var iVersion = addInXml.ToLower().IndexOf(v) + v.Length;
			var iEnd = addInXml.ToLower().IndexOf("\"", iVersion);
			return addInXml.Substring(iVersion, iEnd - iVersion);
		}

		private bool RunMDTool(string rootDir, string arguments)
		{	
			Console.WriteLine("Running mdtool: " + arguments);
			var processStartInfo = new ProcessStartInfo
			{
				FileName = "/Applications/Xamarin Studio.app/Contents/MacOS/mdtool",
				UseShellExecute = false,
				Arguments = arguments,
				WorkingDirectory = rootDir,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				RedirectStandardInput = true,
			};

			var process = Process.Start(processStartInfo);
			var error = process.StandardError.ReadToEnd ();
			var output = process.StandardError.ReadToEnd ();

			process.WaitForExit();

			Console.WriteLine("output: " + output);
			Console.WriteLine("error: " + error);
			Console.WriteLine("exitCode: " + process.ExitCode);

			if (process.ExitCode != 0) {
				Console.WriteLine("mdtool EXCEPTION: exitCode: {0}", process.ExitCode);
				return false;
			}

			return true;
		}

		private void CreateProjectFile(string path, string content, bool overwriteIfExists = false)
		{
			if(CreateFile(path, content, overwriteIfExists))
			{
				// Add file to solution
				proj.AddFile(path);
				proj.Save (null);
			}
		}

		private bool CreateFile(string path, string content, bool overwriteIfExists = false)
		{
			if (!overwriteIfExists && File.Exists (path))
				return false;

			File.WriteAllText (path, content);

			return true;
		}
	}
}