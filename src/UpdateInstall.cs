using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

internal static class UpdateInstall {
 internal static readonly string[] Files={"Nocturne.exe","Nocturne.exe.config","Uninstall.exe","README.md","THIRD-PARTY-NOTICES.md"};
 internal static void Apply(string target,Stream payload){
  using(var guard=new System.Threading.Mutex(false,"Local\\NocturneLocal.Player")){
  bool acquired;
  try{acquired=guard.WaitOne(0);}catch(System.Threading.AbandonedMutexException){acquired=true;}
  if(!acquired)throw new IOException("Close all Nocturne Local windows before updating.");
  try{
  ApplyFiles(target,payload);
  }finally{guard.ReleaseMutex();}
  }
 }
 static void ApplyFiles(string target,Stream payload){
  target=Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
  if(!File.Exists(Path.Combine(target,"Nocturne.exe")))throw new IOException("The existing installation was not found.");
  string work=Path.Combine(target,".nocturne-update-"+Guid.NewGuid().ToString("N"));
  string staged=Path.Combine(work,"new"),backup=Path.Combine(work,"backup");
  Directory.CreateDirectory(staged);
  Directory.CreateDirectory(backup);
  var changed=new List<string>();
  try{
   using(var zip=new ZipArchive(payload,ZipArchiveMode.Read,true)){
    foreach(string name in Files){
     var entry=zip.GetEntry(name);
     if(entry==null)throw new InvalidDataException("Update payload is missing "+name);
     entry.ExtractToFile(Path.Combine(staged,name));
    }
   }
   var current=AssemblyName.GetAssemblyName(Path.Combine(target,"Nocturne.exe")).Version;
   var next=AssemblyName.GetAssemblyName(Path.Combine(staged,"Nocturne.exe")).Version;
   if(next<current)throw new InvalidDataException("A newer version is already installed.");
   foreach(string name in Files){
    string output=Path.Combine(target,name);
    bool exists=File.Exists(output);
    if(exists)File.Replace(Path.Combine(staged,name),output,Path.Combine(backup,name));
    else File.Move(Path.Combine(staged,name),output);
    changed.Add(name);
   }
  }catch{
   foreach(string name in Enumerable.Reverse(changed)){
    string saved=Path.Combine(backup,name),output=Path.Combine(target,name);
    if(File.Exists(saved))File.Replace(saved,output,null);
    else if(File.Exists(output))File.Delete(output);
   }
   throw;
  }finally{
   // All temporary paths are beneath the newly created work directory.
   try{Directory.Delete(work,true);}catch(IOException){}catch(UnauthorizedAccessException){}
  }
 }
 internal static void Run(string target,int parentId){
  try{
   using(var parent=Process.GetProcessById(parentId)){
    if(!string.Equals(parent.MainModule.FileName,Path.Combine(target,"Nocturne.exe"),StringComparison.OrdinalIgnoreCase))throw new IOException("The running app does not match the update location.");
    if(!parent.WaitForExit(30000))throw new IOException("Nocturne did not close. Close it and try again.");
   }
  }catch(ArgumentException){/* The app already exited. */}
  using(var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))Apply(target,payload);
 }
}
