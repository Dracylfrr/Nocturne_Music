using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.IO.Compression;
using Nocturne;
class UpdateTests {
 static string root,installer,oldExe;
 static int checks;
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
 static string Hash(string file){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-","");}
 static Stream Payload(){return Assembly.LoadFile(installer).GetManifestResourceStream("payload.zip");}
 static string Setup(string name){
  string target=Path.Combine(root,name);
  Directory.CreateDirectory(target);
  foreach(string file in UpdateInstall.Files)File.WriteAllText(Path.Combine(target,file),"old "+file);
  File.Copy(oldExe,Path.Combine(target,"Nocturne.exe"),true);
  Directory.CreateDirectory(Path.Combine(target,"Data"));
  File.WriteAllText(Path.Combine(target,"Data","settings.json"),"{\"Favorites\":[\"Music/song.mp3\"],\"DisplayName\":\"My records\"}");
  File.WriteAllText(Path.Combine(target,"library-path.txt"),"custom Music");
  return target;
 }
 static void Main(string[] args){
  root=Path.GetFullPath(args[0]);installer=Path.GetFullPath(args[1]);oldExe=Path.GetFullPath(args[2]);
  Directory.CreateDirectory(root);
  Check(GitHubUpdates.ParseVersion("v1.10")>GitHubUpdates.ParseVersion("1.9"),"Numeric version comparison");
  var digest=Hash(installer);
  string json="{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v1.1\",\"assets\":[{\"name\":\"Nocturne.exe\",\"browser_download_url\":\"https://github.com/Dracylfrr/Nocturne_Music/releases/download/v1.1/Nocturne.exe\",\"size\":"+new FileInfo(installer).Length+",\"digest\":\"sha256:"+digest+"\"}]}";
  var release=GitHubUpdates.ReadRelease(json,new Version(1,0));
  Check(release!=null,"Older installation detects update");
  Check(GitHubUpdates.ReadRelease(json,new Version(1,1))==null,"Same version does not update");
  Check(GitHubUpdates.ReadRelease(json,new Version(2,0))==null,"Newer installation does not downgrade");
  Check(GitHubUpdates.ReadRelease(json.Replace("\"prerelease\":false","\"prerelease\":true"),new Version(1,0))==null,"Ignore prerelease");
  GitHubUpdates.VerifyFile(installer,release);
  release.Digest=new string('0',64);
  bool rejected=false;
  try{GitHubUpdates.VerifyFile(installer,release);}catch(InvalidDataException){rejected=true;}
  Check(rejected,"Reject incorrect download digest");
  string target=Setup("success");
  string settings=Hash(Path.Combine(target,"Data","settings.json"));
  using(var stream=Payload())UpdateInstall.Apply(target,stream);
  Check(AssemblyName.GetAssemblyName(Path.Combine(target,"Nocturne.exe")).Version==new Version(1,1,0,0),"Installed binary upgraded");
  Check(settings==Hash(Path.Combine(target,"Data","settings.json")),"Favorites and custom name preserved");
  Check(File.ReadAllText(Path.Combine(target,"library-path.txt"))=="custom Music","Library path preserved");
  target=Setup("rollback");
  string original=Hash(Path.Combine(target,"Nocturne.exe"));
  using(var locked=new FileStream(Path.Combine(target,"Uninstall.exe"),FileMode.Open,FileAccess.Read,FileShare.Read)){
   rejected=false;
   try{using(var stream=Payload())UpdateInstall.Apply(target,stream);}catch(IOException){rejected=true;}
   Check(rejected,"Locked file prevents update");
  }
  Check(original==Hash(Path.Combine(target,"Nocturne.exe")),"Earlier replaced file rolled back");
  Check(settings==Hash(Path.Combine(target,"Data","settings.json")),"Rollback preserves Favorites");
  target=Setup("bad-payload");
  using(var stream=new MemoryStream()){
   using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)){zip.CreateEntry("../settings.json");}
   stream.Position=0;rejected=false;
   try{UpdateInstall.Apply(target,stream);}catch(InvalidDataException){rejected=true;}
   Check(rejected,"Incomplete or unsafe payload rejected");
  }
  Check(Hash(Path.Combine(target,"Nocturne.exe"))==original,"Invalid payload preserves old app");
  Console.WriteLine("PASS: "+checks+" updater regression checks");
 }
}
