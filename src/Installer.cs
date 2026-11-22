using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

class Installer : Form {
 static readonly string Home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NocturneLocal");
 static readonly string DefaultTarget=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Music","NocturneLocal");
 static string Target=InstalledTarget() ?? DefaultTarget;
 static readonly string MenuLink=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Nocturne Local.lnk");
 static readonly string DesktopLink=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Nocturne Local.lnk");
 const string RegistryPath="Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\NocturneLocal";
 static readonly string LibraryConfig=Path.Combine(Home,"Data","library-path.txt");
 string musicFolder=File.Exists(LibraryConfig)?File.ReadAllText(LibraryConfig).Trim():Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Music");
 Button install,browse;Label status,location;CheckBox shortcut;bool busy;
 [STAThread] static int Main(string[] args){Application.EnableVisualStyles();try{if(Array.IndexOf(args,"--uninstall")>=0){Uninstall();return 0;}if(Array.IndexOf(args,"--verify-payload")>=0){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var zip=new ZipArchive(stream)){if(zip.GetEntry("Nocturne.exe")==null)throw new Exception("Required payload missing");File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"installer-test.txt"),"PASS: installer contains local music player");}return 0;}Application.Run(new Installer());return 0;}catch(Exception ex){MessageBox.Show(ex.Message,"Nocturne Setup");return 1;}}
 Installer(){Target=DefaultTarget;Text="Install Nocturne";ClientSize=new Size(510,500);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(0,0,0);ForeColor=Color.White;Font=new Font("Segoe UI",10);Controls.Add(new Label{Text="nocturne",Font=new Font("Segoe UI",27,FontStyle.Bold),Location=new Point(30,26),AutoSize=true});Controls.Add(new Label{Text="A quiet home for your music.",Location=new Point(34,87),AutoSize=true,ForeColor=Color.LightGray});Controls.Add(new Label{Text="Installs for your Windows account. No administrator needed.\nYour library is in your local Music folder.",Location=new Point(34,129),Size=new Size(450,50)});shortcut=new CheckBox{Text="Create a desktop shortcut",Checked=true,Location=new Point(34,350),AutoSize=true};Controls.Add(shortcut);
  location=new Label{Text=Target,Location=new Point(34,180),Size=new Size(445,42),AutoEllipsis=true};Controls.Add(location);
  browse=new Button{Text="Choose install location…",Location=new Point(32,222),Size=new Size(220,36),BackColor=Color.FromArgb(45,45,45),FlatStyle=FlatStyle.Flat};browse.Click+=(s,e)=>ChooseLocation();Controls.Add(browse);
  var musicLocation=new Label{Text=musicFolder,Location=new Point(34,266),Size=new Size(445,28),AutoEllipsis=true};Controls.Add(musicLocation);
  var chooseMusic=new Button{Text="Choose where to create Music…",Location=new Point(32,298),Size=new Size(260,36),BackColor=Color.FromArgb(45,45,45),FlatStyle=FlatStyle.Flat};
  chooseMusic.Click+=(s,e)=>{
   if(busy||launchReady)return;
   try{
    string folder=ExplorerFolderPicker.Choose(Handle,"Choose a parent folder for Music",Path.GetDirectoryName(musicFolder));
    if(folder==null)return;
    musicFolder=string.Equals(Path.GetFileName(folder),"Music",StringComparison.OrdinalIgnoreCase)?folder:Path.Combine(folder,"Music");
    musicLocation.Text=musicFolder;
   }catch(Exception ex){status.Text=ex.Message;}
  };Controls.Add(chooseMusic);install=new Button{Text="Install Nocturne",Location=new Point(32,390),Size=new Size(180,40),BackColor=Color.FromArgb(45,45,45),FlatStyle=FlatStyle.Flat};install.Click+=async(s,e)=>await Install();Controls.Add(install);status=new Label{Text="A Start menu shortcut is included.",Location=new Point(32,445),Size=new Size(450,30),ForeColor=Color.LightGray};Controls.Add(status);FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};}
 async Task Install(){if(launchReady){Process.Start(Path.Combine(Target,"Nocturne.exe"));Close();return;}busy=true;install.Enabled=false;shortcut.Enabled=false;browse.Enabled=false;status.Text="Installing music player…";bool desktop=shortcut.Checked;try{if(string.Equals(musicFolder,Target,StringComparison.OrdinalIgnoreCase)||musicFolder.StartsWith(Target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Choose a Music folder outside the application folder.");string previous=InstalledTarget();if(Directory.Exists(Target)&&!string.Equals(Target,previous,StringComparison.OrdinalIgnoreCase)&&Directory.GetFileSystemEntries(Target).Length>0&&!(string.Equals(Target,DefaultTarget,StringComparison.OrdinalIgnoreCase)&&File.Exists(Path.Combine(Target,"Nocturne.exe"))&&File.Exists(Path.Combine(Target,"Uninstall.exe"))))throw new Exception("Choose an install location with an empty Nocturne folder.");await Task.Run(()=>{foreach(var proc in Process.GetProcessesByName("Nocturne")){try{if(proc.MainModule.FileName.StartsWith(Target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Quit Nocturne before installing an update.");}catch(System.ComponentModel.Win32Exception){}}Directory.CreateDirectory(Target);using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var zip=new ZipArchive(stream)){foreach(var entry in zip.Entries){string path=Path.GetFullPath(Path.Combine(Target,entry.FullName));if(!path.StartsWith(Target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Invalid installer entry.");if(string.IsNullOrEmpty(entry.Name)){Directory.CreateDirectory(path);continue;}Directory.CreateDirectory(Path.GetDirectoryName(path));entry.ExtractToFile(path,true);}}Directory.CreateDirectory(Home);File.Copy(Path.Combine(Target,"Uninstall.exe"),Path.Combine(Home,"Uninstall.exe"),true);});Directory.CreateDirectory(musicFolder);Directory.CreateDirectory(Path.GetDirectoryName(LibraryConfig));File.WriteAllText(LibraryConfig,musicFolder);string legacyChoice=Path.Combine(Path.GetDirectoryName(LibraryConfig),"use-documents-library");if(File.Exists(legacyChoice))File.Delete(legacyChoice);CreateShortcut(MenuLink);if(desktop)CreateShortcut(DesktopLink);using(var key=Registry.CurrentUser.CreateSubKey(RegistryPath)){key.SetValue("DisplayName","NocturneLocal");key.SetValue("DisplayVersion","1.0");key.SetValue("Publisher","NocturneLocal");key.SetValue("InstallLocation",Target);key.SetValue("DisplayIcon",Path.Combine(Target,"Nocturne.exe"));key.SetValue("UninstallString","\""+Path.Combine(Home,"Uninstall.exe")+"\" --uninstall");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);}status.Text="Installed. Find Nocturne on your desktop or Start menu.";install.Text="Launch Nocturne";install.Enabled=true;busy=false;
 launchReady=true;
 }catch(Exception ex){status.Text=ex.Message;install.Enabled=true;shortcut.Enabled=true;browse.Enabled=true;busy=false;}}
 static string InstalledTarget(){using(var key=Registry.CurrentUser.OpenSubKey(RegistryPath))return key==null?null:key.GetValue("InstallLocation") as string;}
 void ChooseLocation(){
  try {
   string folder=ExplorerFolderPicker.Choose(Handle,"Choose where to install Nocturne",Path.GetDirectoryName(Target));
   if(folder==null)return;
   Target=Path.Combine(folder,"NocturneLocal");
   location.Text=Target;
  } catch(Exception ex){status.Text=ex.Message;}
 }
 bool launchReady;
 protected override void OnClick(EventArgs e){base.OnClick(e);}
 static void CreateShortcut(string path){Type type=Type.GetTypeFromProgID("WScript.Shell");dynamic shell=Activator.CreateInstance(type);dynamic link=shell.CreateShortcut(path);link.TargetPath=Path.Combine(Target,"Nocturne.exe");link.WorkingDirectory=Target;link.Description="Nocturne music player";link.Save();System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
 static void Uninstall(){if(MessageBox.Show("Uninstall Nocturne? Your music, artwork and settings will be kept.","Nocturne",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;foreach(var proc in Process.GetProcessesByName("Nocturne")){try{if(proc.MainModule.FileName.StartsWith(Target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Quit Nocturne before uninstalling.");}catch(System.ComponentModel.Win32Exception){}}if(File.Exists(Path.Combine(Target,"Nocturne.exe"))&&File.Exists(Path.Combine(Target,"Uninstall.exe")))Directory.Delete(Target,true);if(File.Exists(MenuLink))File.Delete(MenuLink);if(File.Exists(DesktopLink))File.Delete(DesktopLink);Registry.CurrentUser.DeleteSubKeyTree(RegistryPath,false);MessageBox.Show("Nocturne has been removed. Your music and settings are preserved.","NocturneLocal");}
}




