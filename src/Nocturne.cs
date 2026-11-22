using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Nocturne {
 public class Track : INotifyPropertyChanged {
  public string Path {get;set;} public string Title {get;set;} public string Artist {get;set;} public string Section {get;set;} public DateTime Added {get;set;}
  bool isFavorite;
  public event PropertyChangedEventHandler PropertyChanged;
  public bool IsFavorite {
   get {return isFavorite;}
   set {
    if(isFavorite==value)return;
    isFavorite=value;
    var changed=PropertyChanged;
    if(changed==null)return;
    changed(this,new PropertyChangedEventArgs("FavoriteStar"));
    changed(this,new PropertyChangedEventArgs("FavoriteAction"));
   }
  }
  public string FavoriteStar {get {return IsFavorite?"★":"☆";}}
  public string FavoriteAction {get {return IsFavorite?"Remove from Favorites":"Add to Favorites";}}
  public string AddedLabel {get {return Added.ToLocalTime().ToString("MMM d, yyyy");}}
  public string AddedDetail {get {return Added.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");}}
 }
 public class Preferences {
  public string DisplayName="Nocturne";
  public bool CoverOnlySections;
  public bool ApplyTrackArtworkToSection;
  public bool Tray = false; public bool MiniCover = true; public bool AlwaysOnTop = false; public int Sort = 0; public double Volume = .7;
  public List<string> Favorites = new List<string>();
  public Dictionary<string,List<string>> SectionOrder = new Dictionary<string,List<string>>();
  public Dictionary<string,DateTime> Added = new Dictionary<string,DateTime>(StringComparer.OrdinalIgnoreCase);
 }
 public class Video { public string PlaylistSection {get;set;} public int Number {get;set;} public string QueueLabel {get{return Number+". "+Title+"  ✓";}} public string Title {get;set;} public string Artist {get;set;} public string Url {get;set;} public string Thumbnail {get;set;} public override string ToString(){return Title + "\n" + Artist;} }
 public static class Storage {
  public static readonly string Data = Environment.GetEnvironmentVariable("NOCTURNE_TEST_DATA") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NocturneLocal", "Data");
  public static readonly string DefaultLibrary = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Music");
  public static readonly string PreviousLibrary = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Documents","Music");
  public static readonly string LibraryChoice = System.IO.Path.Combine(Data,"use-documents-library");
  public static readonly string Library = Environment.GetEnvironmentVariable("NOCTURNE_TEST_LIBRARY") ?? ReadLibrary();
  public static readonly string Sections = Library;
  static string ReadLibrary(){
   if(File.Exists(LibraryChoice))return PreviousLibrary;
   string config=System.IO.Path.Combine(Data,"library-path.txt");
   try{
    if(File.Exists(config)){
     string path=File.ReadAllText(config).Trim();
     if(System.IO.Path.IsPathRooted(path))return System.IO.Path.GetFullPath(path);
    }
   }catch(IOException){}catch(UnauthorizedAccessException){}
   return DefaultLibrary;
  }
  public static readonly string Tools = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
  public static readonly JavaScriptSerializer Json = new JavaScriptSerializer {MaxJsonLength = 20 * 1024 * 1024};
  public static void Atomic(string path, string value) {Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)); string temp=path+".tmp"; File.WriteAllText(temp,value,Encoding.UTF8); if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);}
  static readonly string AppDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar);
  static readonly string LibraryAppDirectory = System.IO.Path.Combine(Library,"Nocturne");
  static bool AtOrBelow(string path,string folder){
   return path.Equals(folder,StringComparison.OrdinalIgnoreCase)||path.StartsWith(folder+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
  }
  public static bool Inside(string path){
   string full=System.IO.Path.GetFullPath(path);
   if(!AtOrBelow(full,Library))return false;
   if(AtOrBelow(AppDirectory,Library)&&AtOrBelow(full,AppDirectory))return false;
   return !AtOrBelow(full,LibraryAppDirectory)||!File.Exists(System.IO.Path.Combine(LibraryAppDirectory,"Nocturne.exe"));
  }
  public static string Relative(string path) {return path.Length <= Library.Length ? "Library" : path.Substring(Library.Length+1);}
  public static string Child(string parent,string name) {
   if(string.IsNullOrWhiteSpace(name)||name=="."||name==".."||name.EndsWith(".")||name.EndsWith(" ")||name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())>=0) throw new Exception("Choose a folder name without slashes or special characters.");
   string p=System.IO.Path.GetFullPath(System.IO.Path.Combine(parent,name)); if(!Inside(p))throw new Exception("Choose a location inside your music library."); return p;
  }
  public static string Unique(string path) {if(!File.Exists(path))return path; string dir=System.IO.Path.GetDirectoryName(path),name=System.IO.Path.GetFileNameWithoutExtension(path),ext=System.IO.Path.GetExtension(path); for(int i=2;;i++){string p=System.IO.Path.Combine(dir,name+" ("+i+")"+ext);if(!File.Exists(p))return p;}}
  public static string[] AudioExtensions={".mp3",".m4a",".wav",".wma",".aac"};
  public static IEnumerable<string> Audio(string folder) {
   foreach(string f in Directory.GetFiles(folder))if(AudioExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))yield return f;
   foreach(string d in Directory.GetDirectories(folder)){if(!Inside(d)||(File.GetAttributes(d)&FileAttributes.ReparsePoint)!=0)continue; foreach(string f in Audio(d))yield return f;}
  }
 }
 public class Player {
  Window window; Preferences prefs; MediaPlayer media=new MediaPlayer(); Forms.NotifyIcon tray; FileSystemWatcher watcher; DispatcherTimer refreshTimer,timer;
  List<Track> all=new List<Track>(),visible=new List<Track>(),queue=new List<Track>(); Track current; string selected=Storage.Library,artistFilter=null; int index=-1,favoritesSort=2; bool changingSort,favoritesView,playing,mini,shuffle,repeat,seeking,refreshing,closing; double fullWidth=1180,fullHeight=780; Random random=new Random(); Task scanTask;
  T C<T>(string name) where T:class {return window.FindName(name) as T;}
  void Click(string name,Action action){C<Button>(name).Click+=(s,e)=>Safe(action);}
  void Safe(Action action){try{action();}catch(Exception ex){MessageBox.Show(window,ex.Message,"Nocturne",MessageBoxButton.OK,MessageBoxImage.Information);}}
  public Player(){
   Directory.CreateDirectory(Storage.Sections); Directory.CreateDirectory(Storage.Data);
   try{prefs=Storage.Json.Deserialize<Preferences>(File.ReadAllText(System.IO.Path.Combine(Storage.Data,"settings.json")));}catch{prefs=new Preferences();}
   if(prefs==null)prefs=new Preferences(); if(prefs.Added==null)prefs.Added=new Dictionary<string,DateTime>();
   if(prefs.Favorites==null)prefs.Favorites=new List<string>();
   if(prefs.SectionOrder==null)prefs.SectionOrder=new Dictionary<string,List<string>>();
   using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml"))window=(Window)XamlReader.Load(stream);
   ApplyDisplayName();
   window.Icon=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location).Handle,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
   C<System.Windows.Controls.Border>("TitleBar").MouseLeftButtonDown+=(s,e)=>{if(e.LeftButton==MouseButtonState.Pressed)window.DragMove();};
   Click("CloseApp",()=>window.Close()); Click("Minimize",Minimize); Click("Compact",ToggleMini);
   Click("AllMusic",()=>Select(Storage.Library,null)); Click("Artists",ShowArtists); Click("Favorites",()=>{C<TextBox>("Search").Clear();Select(Storage.Library,null,true);BuildTree();}); Click("NewFolder",NewFolder); Click("Settings",ShowSettings);
   Click("OpenFolder",()=>Open(selected)); Click("DeleteSection",()=>DeleteSection(selected)); Click("Import",Import); Click("RefreshLibrary",Refresh); Click("PlaySection",()=>{if(visible.Count>0)Start(visible,0);});
   Click("PlayPause",Pause); Click("Previous",()=>Advance(-1));Click("Next",()=>Advance(1));
   Click("Shuffle",()=>{shuffle=!shuffle;C<Button>("Shuffle").Foreground=new SolidColorBrush(shuffle?Colors.White:Colors.Gray);C<Button>("Shuffle").ToolTip=shuffle?"Shuffle on":"Shuffle off";});
   Click("Repeat",()=>{repeat=!repeat;C<Button>("Repeat").Foreground=new SolidColorBrush(repeat?Colors.White:Colors.Gray);C<Button>("Repeat").ToolTip=repeat?"Repeat section on":"Repeat off";});
   Click("SectionArt",()=>Artwork(selected,false)); Click("TrackArt",()=>{if(current!=null)Artwork(current.Path,true);});
   ConfigureTrackMoves();
   C<ListBox>("Tracks").MouseDoubleClick+=(s,e)=>{int i=C<ListBox>("Tracks").SelectedIndex;if(i>=0)Start(visible,i);};
   C<ListBox>("Tracks").AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,new RoutedEventHandler((sender,args)=>{
    var star=(args.OriginalSource as Button)??(args.Source as Button);
    if(star==null||!Equals(star.Tag,"Favorite"))return;
    var track=star.DataContext as Track;
    if(track==null)return;
    track.IsFavorite=!track.IsFavorite;
    if(track.IsFavorite)prefs.Favorites.Add(track.Path);
    else prefs.Favorites.RemoveAll(path=>path.Equals(track.Path,StringComparison.OrdinalIgnoreCase));
    Save();
    if(favoritesView)Render();
    else UpdateFavoritesCount();
   }),true);
   C<TextBox>("Search").TextChanged+=(s,e)=>Render();
   C<ComboBox>("Sort").SelectedIndex=prefs.Sort;
   C<ComboBox>("Sort").SelectionChanged+=(s,e)=>{
    if(changingSort)return;
    int sort=C<ComboBox>("Sort").SelectedIndex;
    if(sort<0)return;
    if(favoritesView)favoritesSort=sort;
    else{prefs.Sort=sort;Save();BuildTree();}
    Render();
   };
   C<TreeView>("Folders").SelectedItemChanged+=(s,e)=>{var item=e.NewValue as TreeViewItem;if(item!=null&&!refreshing)Select((string)item.Tag,null);};
   var sectionRoot=C<TextBlock>("SectionsRoot");
   sectionRoot.AllowDrop=true;
   sectionRoot.DragOver+=(sender,args)=>SectionDragOver(args,Storage.Sections);
   sectionRoot.DragLeave+=(sender,args)=>sectionRoot.Background=Brushes.Transparent;
   sectionRoot.Drop+=(sender,args)=>{sectionRoot.Background=Brushes.Transparent;SectionDrop(args,Storage.Sections,null,false);};
   C<Slider>("Volume").Value=prefs.Volume;media.Volume=prefs.Volume;
   C<Slider>("Volume").ValueChanged+=(s,e)=>{media.Volume=C<Slider>("Volume").Value;prefs.Volume=media.Volume;};
   C<Slider>("Seek").PreviewMouseLeftButtonDown+=(s,e)=>seeking=true;
   C<Slider>("Seek").PreviewMouseLeftButtonUp+=(s,e)=>{if(current!=null)media.Position=TimeSpan.FromSeconds(C<Slider>("Seek").Value);seeking=false;};
   C<Slider>("Seek").LostMouseCapture+=(s,e)=>{if(seeking&&current!=null)media.Position=TimeSpan.FromSeconds(C<Slider>("Seek").Value);seeking=false;};
   C<Slider>("Seek").PreviewKeyUp+=(s,e)=>{if(current!=null)media.Position=TimeSpan.FromSeconds(C<Slider>("Seek").Value);};
   media.MediaOpened+=(s,e)=>{C<Slider>("Seek").Maximum=media.NaturalDuration.HasTimeSpan?media.NaturalDuration.TimeSpan.TotalSeconds:1;C<TextBlock>("Duration").Text=media.NaturalDuration.HasTimeSpan?TimeLabel(media.NaturalDuration.TimeSpan):"0:00";};
   media.MediaEnded+=(s,e)=>Advance(1);media.MediaFailed+=(s,e)=>{playing=false;C<Button>("PlayPause").Content="▶";SetStatus("Cannot play this file: "+e.ErrorException.Message);};
   timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(400)};timer.Tick+=(s,e)=>{if(current!=null&&!seeking){C<Slider>("Seek").Value=media.Position.TotalSeconds;C<TextBlock>("Elapsed").Text=TimeLabel(media.Position);}};timer.Start();
   SetupTray();
   refreshTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(900)}; refreshTimer.Tick+=(s,e)=>{refreshTimer.Stop();Refresh();};
   watcher=new FileSystemWatcher(Storage.Library){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite};
   FileSystemEventHandler changed=(s,e)=>{if(!closing)window.Dispatcher.BeginInvoke(new Action(()=>{refreshTimer.Stop();refreshTimer.Start();}));};watcher.Created+=changed;watcher.Deleted+=changed;watcher.Changed+=changed;watcher.Renamed+=(s,e)=>changed(s,e);watcher.Error+=(s,e)=>window.Dispatcher.BeginInvoke(new Action(()=>Refresh()));watcher.EnableRaisingEvents=true;
   window.StateChanged+=(s,e)=>{if(window.WindowState==WindowState.Minimized&&prefs.Tray){window.Hide();tray.Visible=true;}};
   window.Closing+=(s,e)=>{closing=true;media.Stop();media.Close();watcher.Dispose();timer.Stop();refreshTimer.Stop();tray.Visible=false;tray.Dispose();Save();};
   window.Loaded+=(s,e)=>Refresh();
  }
  public void Run(){new Application().Run(window);}
  void Save(){try{Storage.Atomic(System.IO.Path.Combine(Storage.Data,"settings.json"),Storage.Json.Serialize(prefs));}catch(Exception ex){SetStatus("Settings could not be saved: "+ex.Message);}}
  void SetStatus(string text){C<TextBlock>("Status").Text=text;}
  static string TimeLabel(TimeSpan value){return ((int)value.TotalMinutes).ToString()+":"+value.Seconds.ToString("00");}
  public static void Open(string path){Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
  void SetupTray(){tray=new Forms.NotifyIcon{Icon=System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),Text="Nocturne",Visible=false};var menu=new Forms.ContextMenuStrip();menu.Items.Add("Open Nocturne",null,(s,e)=>Restore());menu.Items.Add("Play / pause",null,(s,e)=>Pause());menu.Items.Add("Next track",null,(s,e)=>Advance(1));menu.Items.Add("Quit",null,(s,e)=>window.Close());tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>Restore();}
  void Restore(){window.Show();window.WindowState=WindowState.Normal;window.Activate();tray.Visible=false;}
  void Minimize(){if(prefs.Tray){tray.Visible=true;window.Hide();}else window.WindowState=WindowState.Minimized;}
  void ToggleMini(){mini=!mini;if(mini){fullWidth=window.Width;fullHeight=window.Height;C<Grid>("LibraryView").Visibility=Visibility.Collapsed;window.MinWidth=440;window.MinHeight=156;window.Width=560;window.Height=156;window.ResizeMode=ResizeMode.NoResize;((Grid)window.Content).RowDefinitions[1].Height=new GridLength(0);C<StackPanel>("VolumePanel").Visibility=Visibility.Collapsed;C<ColumnDefinition>("VolumeColumn").Width=new GridLength(0);C<TextBlock>("MiniLabel").Visibility=Visibility.Visible;}else{window.ResizeMode=ResizeMode.CanResizeWithGrip;window.MinWidth=850;window.MinHeight=560;window.Width=fullWidth;window.Height=fullHeight;C<Grid>("LibraryView").Visibility=Visibility.Visible;((Grid)window.Content).RowDefinitions[1].Height=new GridLength(1,GridUnitType.Star);C<StackPanel>("VolumePanel").Visibility=Visibility.Visible;C<ColumnDefinition>("VolumeColumn").Width=new GridLength(180);C<TextBlock>("MiniLabel").Visibility=Visibility.Collapsed;}ApplyMini();}
  void ApplyMini(){window.Topmost=mini&&prefs.AlwaysOnTop;bool show=!mini||prefs.MiniCover;C<Border>("NowArtBorder").Visibility=show?Visibility.Visible:Visibility.Collapsed;C<ColumnDefinition>("CoverColumn").Width=new GridLength(show?70:0);}
  async void Refresh(){
   if(scanTask!=null&&!scanTask.IsCompleted){refreshTimer.Start();return;} SetStatus("Reading your library…");
   var oldAdded=new Dictionary<string,DateTime>(prefs.Added,StringComparer.OrdinalIgnoreCase);
   var task=Task.Run(()=>{var result=new List<Track>();foreach(string path in Storage.Audio(Storage.Library)){try{DateTime added; if(!oldAdded.TryGetValue(path,out added))added=File.GetCreationTimeUtc(path);result.Add(ReadTrack(path,added));}catch{}}return result;});scanTask=task;
   try{var result=await task;if(closing)return;all=result;foreach(Track t in all)prefs.Added[t.Path]=t.Added;Save();if(!Directory.Exists(selected))selected=Storage.Library;BuildTree();Render();SetStatus(all.Count+" tracks · "+Storage.Library);}catch(Exception ex){if(!closing)SetStatus("Library could not be read: "+ex.Message);}
  }
  static Track ReadTrack(string path,DateTime added){var t=new Track{Path=path,Title=System.IO.Path.GetFileNameWithoutExtension(path),Artist="Unknown artist",Section=Storage.Relative(System.IO.Path.GetDirectoryName(path)),Added=added};
   string info=System.IO.Path.ChangeExtension(path,".info.json");if(File.Exists(info)){try{var d=Storage.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(info));t.Title=Get(d,"track",Get(d,"title",t.Title));t.Artist=Get(d,"artist",Get(d,"creator",Get(d,"uploader",t.Artist)));}catch{}}
   if(System.IO.Path.GetExtension(path).Equals(".mp3",StringComparison.OrdinalIgnoreCase))ReadId3(path,t);return t;
  }
  static int Synch(byte[] b,int i){return (b[i]<<21)|(b[i+1]<<14)|(b[i+2]<<7)|b[i+3];}
  static int Big(byte[] b,int i){return (b[i]<<24)|(b[i+1]<<16)|(b[i+2]<<8)|b[i+3];}
  static void ReadId3(string path,Track t){try{using(var f=File.OpenRead(path)){byte[] h=new byte[10];if(f.Read(h,0,10)!=10||Encoding.ASCII.GetString(h,0,3)!="ID3"||h[3]<3||h[3]>4)return;int size=Synch(h,6);if(size>16*1024*1024)return;byte[] b=new byte[size];if(f.Read(b,0,size)!=size)return;int pos=0;if((h[5]&64)!=0&&size>4)pos=h[3]==4?Synch(b,0):Big(b,0)+4;
    while(pos+10<=size){string id=Encoding.ASCII.GetString(b,pos,4);int len=h[3]==4?Synch(b,pos+4):Big(b,pos+4);if(len<=0||pos+10+len>size)break;if(id=="TIT2"||id=="TPE1"){int p=pos+10;Encoding enc=b[p]==3?Encoding.UTF8:b[p]==1?Encoding.Unicode:b[p]==2?Encoding.BigEndianUnicode:Encoding.GetEncoding(28591);string value=enc.GetString(b,p+1,len-1).Trim('\0','\uFEFF',' ');if(value.Length>0){if(id=="TIT2")t.Title=value;else t.Artist=value;}}pos+=10+len;}
   }}catch{}}
  public static string Get(Dictionary<string,object> d,string key,string fallback){object value;return d.TryGetValue(key,out value)&&value!=null&&!string.IsNullOrWhiteSpace(value.ToString())?value.ToString():fallback;}
  void BuildTree(){
   refreshing=true;
   try{
    var nodes=C<TreeView>("Folders").Items;
    var collapsed=new HashSet<string>(CollapsedSections(nodes),StringComparer.OrdinalIgnoreCase);
    nodes.Clear();
    AddNodes(nodes,Storage.Library,collapsed);
   }catch(Exception ex){SetStatus(ex.Message);}finally{refreshing=false;}
  }
  static IEnumerable<string> CollapsedSections(ItemCollection nodes){
   foreach(TreeViewItem node in nodes.OfType<TreeViewItem>()){
    if(!node.IsExpanded)yield return (string)node.Tag;
    foreach(string path in CollapsedSections(node.Items))yield return path;
   }
  }
  string AddNodes(ItemCollection target,string folder,HashSet<string> collapsed){
   string firstCover=null;
   IEnumerable<string> dirs=Directory.GetDirectories(folder).Where(d=>!SamePath(d,Storage.Sections)&&Storage.Inside(d)&&(File.GetAttributes(d)&FileAttributes.ReparsePoint)==0);
   dirs=prefs.Sort==1?dirs.OrderByDescending(d=>all.Where(t=>t.Path.StartsWith(d+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).Select(t=>t.Added).DefaultIfEmpty(Directory.GetCreationTimeUtc(d)).Max()).ThenBy(d=>d,StringComparer.CurrentCultureIgnoreCase):dirs.OrderBy(d=>d,StringComparer.CurrentCultureIgnoreCase);
   List<string> order;
   if(prefs.SectionOrder.TryGetValue(folder,out order)&&order!=null){
    var positions=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
    for(int i=0;i<order.Count;i++)positions[order[i]]=i;
    dirs=dirs.OrderBy(path=>positions.ContainsKey(path)?positions[path]:int.MaxValue);
   }
   foreach(string path in dirs){
    string name=System.IO.Path.GetFileName(path);
    var header=new Border{Background=Brushes.Transparent,AllowDrop=true,Padding=new Thickness(6),CornerRadius=new CornerRadius(7),ToolTip=name};
    if(prefs.CoverOnlySections)header.HorizontalAlignment=HorizontalAlignment.Left;
    var content=new DockPanel();
    var cover=new Image{Width=34,Height=34,Stretch=Stretch.UniformToFill};
    var coverSlot=new Grid{Width=34,Height=34,Margin=prefs.CoverOnlySections?new Thickness(0):new Thickness(0,0,8,0),Background=new SolidColorBrush(Color.FromRgb(46,46,46)),ClipToBounds=true};
    coverSlot.Children.Add(new TextBlock{Text="♫",Foreground=Brushes.Gray,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontSize=16});
    coverSlot.Children.Add(cover);
    DockPanel.SetDock(coverSlot,Dock.Left);
    content.Children.Add(coverSlot);
    if(!prefs.CoverOnlySections)content.Children.Add(new TextBlock{Text=name,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,FontSize=12});
    header.Child=content;
    System.Windows.Automation.AutomationProperties.SetName(header,name);
    var node=new TreeViewItem{Header=header,Tag=path,IsExpanded=!collapsed.Contains(path),IsSelected=path.Equals(selected,StringComparison.OrdinalIgnoreCase)};
    Point start=new Point();
    bool canDrag=false;
    header.PreviewMouseLeftButtonDown+=(sender,args)=>{start=args.GetPosition(header);canDrag=true;};
    header.PreviewMouseLeftButtonUp+=(sender,args)=>canDrag=false;
    node.PreviewMouseMove+=(sender,args)=>{
     if(!canDrag||args.LeftButton!=MouseButtonState.Pressed){canDrag=false;return;}
     Point point=args.GetPosition(header);
     if(Math.Abs(point.X-start.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance)return;
     canDrag=false;
     DragDrop.DoDragDrop(header,new DataObject("Nocturne.Section",path),DragDropEffects.Move);
    };
    header.DragOver+=(sender,args)=>{
     double fraction=args.GetPosition(header).Y/header.ActualHeight;
     if(args.Data.GetDataPresent("Nocturne.Track")){
      var track=args.Data.GetData("Nocturne.Track") as Track;
      args.Effects=track!=null&&!SamePath(System.IO.Path.GetDirectoryName(track.Path),path)?DragDropEffects.Move:DragDropEffects.None;
      args.Handled=true;
     }else SectionDragOver(args,fraction>=.25&&fraction<=.75?path:folder,path);
     header.Background=args.Effects==DragDropEffects.Move?Brushes.DimGray:Brushes.Transparent;
    };
    header.DragLeave+=(sender,args)=>header.Background=Brushes.Transparent;
    header.Drop+=(sender,args)=>{
     header.Background=Brushes.Transparent;
     var track=args.Data.GetData("Nocturne.Track") as Track;
     if(track!=null){args.Handled=true;Safe(()=>MoveTrack(track,path));return;}
     double fraction=args.GetPosition(header).Y/header.ActualHeight;
     if(fraction>=.25&&fraction<=.75)SectionDrop(args,path,null,false);
     else SectionDrop(args,folder,path,fraction>.75);
    };
    var menu=new ContextMenu();
    if(!SamePath(folder,Storage.Library)&&!SamePath(folder,Storage.Sections)){
     var up=new MenuItem{Header="Move up one level"};
     up.Click+=(sender,args)=>{
      string parent=System.IO.Path.GetDirectoryName(folder);
      MoveSectionAfterScan(path,SamePath(parent,Storage.Library)?Storage.Sections:parent,null,false);
     };
     menu.Items.Add(up);
    }
    var rename=new MenuItem{Header="Rename section…"};
    rename.Click+=(sender,args)=>Safe(()=>{
     string newSectionName=Prompt("Rename section","Enter a new section name.",System.IO.Path.GetFileName(path));
     if(!string.IsNullOrWhiteSpace(newSectionName)&&newSectionName!=System.IO.Path.GetFileName(path))MoveSectionAfterScan(path,folder,null,false,newSectionName);
    });
    menu.Items.Add(rename);
    var remove=new MenuItem{Header="Delete section…"};
    remove.Click+=(sender,args)=>Safe(()=>DeleteSection(path));
    menu.Items.Add(remove);
    node.ContextMenu=menu;
    if(prefs.CoverOnlySections)node.HorizontalContentAlignment=HorizontalAlignment.Left;
    target.Add(node);
    string childCover=AddNodes(node.Items,path,collapsed);
    string imagePath=FindSectionCover(path)??childCover;
    SetImage(cover,imagePath,72);
    if(firstCover==null)firstCover=imagePath;
   }
   return firstCover;
  }
  static bool SamePath(string first,string second){return string.Equals(first,second,StringComparison.OrdinalIgnoreCase);}
  static bool IsWithin(string path,string folder){
   return SamePath(path,folder)||path.StartsWith(folder+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
  }
  static string RebasePath(string path,string source,string destination){
   return IsWithin(path,source)?destination+path.Substring(source.Length):path;
  }
  static bool CanDropSection(string source,string parent,string anchor){
   return source!=null&&Storage.Inside(source)&&!SamePath(source,Storage.Library)&&Directory.Exists(source)
    &&!SamePath(source,anchor)&&!IsWithin(parent,source);
  }
  void SectionDragOver(DragEventArgs args,string parent,string anchor=null){
   var source=args.Data.GetData("Nocturne.Section") as string;
   args.Effects=CanDropSection(source,parent,anchor)?DragDropEffects.Move:DragDropEffects.None;
   var label=args.Source as TextBlock;
   if(label!=null)label.Background=args.Effects==DragDropEffects.Move?Brushes.DimGray:Brushes.Transparent;
   args.Handled=true;
  }
  void SectionDrop(DragEventArgs args,string parent,string anchor,bool after){
   args.Handled=true;
   var source=args.Data.GetData("Nocturne.Section") as string;
   if(!CanDropSection(source,parent,anchor))return;
   MoveSectionAfterScan(source,parent,anchor,after);
  }
  async void MoveSectionAfterScan(string source,string parent,string anchor,bool after,string newName=null){
   if(scanTask!=null&&!scanTask.IsCompleted){
    try{await scanTask;}catch{/* Refresh reports scan errors. */}
   }
   if(!closing&&CanDropSection(source,parent,anchor))Safe(()=>MoveSection(source,parent,anchor,after,newName));
  }
  void MoveSection(string source,string parent,string anchor,bool after,string newName=null){
    var siblings=SectionSiblings(C<TreeView>("Folders").Items,parent).ToList();
    string oldParent=System.IO.Path.GetDirectoryName(source);
    string destination=Storage.Child(parent,newName??System.IO.Path.GetFileName(source));
    if(!string.Equals(source,destination,StringComparison.Ordinal)){
     if(!SamePath(source,destination)&&(Directory.Exists(destination)||File.Exists(destination)))throw new IOException("A section with that name already exists there.");
     bool movingCurrent=current!=null&&IsWithin(current.Path,source);
     bool resume=playing;
     if(movingCurrent)media.Close();
     try{
      if(SamePath(source,destination)){
       string temporary=Storage.Child(parent,Guid.NewGuid().ToString("N"));
       Directory.Move(source,temporary);
       try{Directory.Move(temporary,destination);}catch{Directory.Move(temporary,source);throw;}
      }else Directory.Move(source,destination);
     }
     catch{if(movingCurrent){PlayCurrent();if(!resume)Pause();}throw;}
     selected=RebasePath(selected,source,destination);
     prefs.Favorites=prefs.Favorites.Select(path=>RebasePath(path,source,destination)).ToList();
     foreach(var entry in prefs.Added.Where(entry=>IsWithin(entry.Key,source)).ToArray()){
      prefs.Added.Remove(entry.Key);
      prefs.Added[RebasePath(entry.Key,source,destination)]=entry.Value;
     }
     var previousOrder=prefs.SectionOrder.ToArray();
     prefs.SectionOrder.Clear();
     foreach(var entry in previousOrder.OrderBy(entry=>IsWithin(entry.Key,source))){
      prefs.SectionOrder[RebasePath(entry.Key,source,destination)]=(entry.Value??new List<string>()).Select(path=>RebasePath(path,source,destination)).ToList();
     }
     List<string> oldOrder;
     if(prefs.SectionOrder.TryGetValue(oldParent,out oldOrder))oldOrder.RemoveAll(path=>SamePath(path,destination));
     foreach(Track track in all.Concat(queue).Distinct()){
      if(!IsWithin(track.Path,source))continue;
      track.Path=RebasePath(track.Path,source,destination);
      track.Section=Storage.Relative(System.IO.Path.GetDirectoryName(track.Path));
     }
     if(movingCurrent){
      PlayCurrent();
      if(!resume)Pause();
     }
    }
    var updated=siblings.Where(path=>!SamePath(path,source)&&!SamePath(path,destination)).ToList();
    int position=newName!=null?siblings.FindIndex(path=>SamePath(path,source)):anchor==null?updated.Count:updated.FindIndex(path=>SamePath(path,anchor));
    if(position<0)position=updated.Count;
    else if(anchor!=null&&after)position++;
    updated.Insert(position,destination);
    prefs.SectionOrder[parent]=updated;
    Save();
    window.Dispatcher.BeginInvoke(new Action(()=>{BuildTree();Render();Refresh();}));
  }
  IEnumerable<string> SectionSiblings(ItemCollection nodes,string parent){
   foreach(TreeViewItem node in nodes.OfType<TreeViewItem>()){
    string path=(string)node.Tag;
    if(SamePath(System.IO.Path.GetDirectoryName(path),parent))yield return path;
    else if(IsWithin(parent,path)){
     foreach(string child in SectionSiblings(node.Items,parent))yield return child;
    }
   }
  }

  void ConfigureTrackMoves(){
   var list=C<ListBox>("Tracks");
   Track dragged=null;
   Point start=new Point();
   list.PreviewMouseLeftButtonDown+=(sender,args)=>{
    var origin=args.OriginalSource as DependencyObject;
    var row=origin==null?null:ItemsControl.ContainerFromElement(list,origin) as ListBoxItem;
    dragged=row==null?null:row.Content as Track;
    for(var element=origin;element!=null&&element!=row;element=element is Visual?VisualTreeHelper.GetParent(element):null){
     if(element is Button){dragged=null;break;}
    }
    start=args.GetPosition(list);
   };
   list.PreviewMouseMove+=(sender,args)=>{
    if(args.LeftButton!=MouseButtonState.Pressed){dragged=null;return;}
    if(dragged==null)return;
    Point point=args.GetPosition(list);
    if(Math.Abs(point.X-start.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance)return;
    Track track=dragged;
    dragged=null;
    DragDrop.DoDragDrop(list,new DataObject("Nocturne.Track",track),DragDropEffects.Move);
   };
   list.AllowDrop=true;
   list.PreviewDragOver+=(sender,args)=>FavoriteDrop(list,args,false);
   list.PreviewDrop+=(sender,args)=>FavoriteDrop(list,args,true);
   list.PreviewMouseRightButtonDown+=(sender,args)=>{
    var origin=args.OriginalSource as DependencyObject;
    var row=origin==null?null:ItemsControl.ContainerFromElement(list,origin) as ListBoxItem;
    var track=row==null?null:row.Content as Track;
    if(track==null)return;
    var menu=new ContextMenu();
    var artwork=new MenuItem{Header="Change artwork…"};
    artwork.Click+=(s,e)=>Safe(()=>Artwork(track.Path,true));
    menu.Items.Add(artwork);
    var move=new MenuItem{Header="Move to section…"};
    move.Click+=(s,e)=>Safe(()=>ChooseTrackDestination(track));
    menu.Items.Add(move);
    menu.PlacementTarget=row;
    menu.IsOpen=true;
    args.Handled=true;
   };
  }
  void FavoriteDrop(ListBox list,DragEventArgs args,bool commit){
   if(!favoritesView||favoritesSort!=2||!args.Data.GetDataPresent("Nocturne.Track"))return;
   var origin=args.OriginalSource as DependencyObject;
   var row=origin==null?null:ItemsControl.ContainerFromElement(list,origin) as ListBoxItem;
   var target=row==null?null:row.Content as Track;
   var source=args.Data.GetData("Nocturne.Track") as Track;
   bool valid=source!=null&&target!=null&&source.IsFavorite&&target.IsFavorite&&!SamePath(source.Path,target.Path);
   args.Effects=valid?DragDropEffects.Move:DragDropEffects.None;
   args.Handled=true;
   if(commit&&valid)ReorderFavorite(source.Path,target.Path,args.GetPosition(row).Y>row.ActualHeight/2);
  }
  void ReorderFavorite(string source,string target,bool after){
   int indexOfSource=prefs.Favorites.FindIndex(path=>SamePath(path,source));
   int indexOfTarget=prefs.Favorites.FindIndex(path=>SamePath(path,target));
   if(indexOfSource<0||indexOfTarget<0||indexOfSource==indexOfTarget)return;
   prefs.Favorites.RemoveAt(indexOfSource);
   if(indexOfSource<indexOfTarget)indexOfTarget--;
   prefs.Favorites.Insert(indexOfTarget+(after?1:0),source);
   Save();
   Render();
  }

  void ChooseTrackDestination(Track track){
   var dialog=Dialog("Move to section",460,420);
   var panel=Panel(dialog);
   panel.Children.Add(Label(track.Title,16));
   var choices=new ListBox{Height=250};
   choices.Items.Add("Library (no section)");
   foreach(string path in SectionPaths(C<TreeView>("Folders").Items))choices.Items.Add(Storage.Relative(path));
   panel.Children.Add(choices);
   panel.Children.Add(Button("Move",()=>{
    var choice=choices.SelectedItem as string;
    if(choice==null)return;
    string folder=choices.SelectedIndex==0?Storage.Library:System.IO.Path.Combine(Storage.Library,choice);
    MoveTrack(track,folder);
    dialog.Close();
   }));
   dialog.ShowDialog();
  }
  static IEnumerable<string> SectionPaths(ItemCollection nodes){
   foreach(TreeViewItem node in nodes.OfType<TreeViewItem>()){
    yield return (string)node.Tag;
    foreach(string path in SectionPaths(node.Items))yield return path;
   }
  }
  void MoveTrack(Track track,string folder){
   string source=track.Path;
   if(!Storage.Inside(source)||!Storage.Inside(folder)||!Directory.Exists(folder))throw new IOException("The song or section is unavailable.");
   if(SamePath(System.IO.Path.GetDirectoryName(source),folder))return;
   string stem=System.IO.Path.GetFileNameWithoutExtension(source);
   string extension=System.IO.Path.GetExtension(source);
   var files=new List<KeyValuePair<string,string>>{new KeyValuePair<string,string>(source,extension)};
   foreach(string suffix in new[]{".info.json",".jpg",".png",".jpeg"}){
    string sidecar=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source),stem+suffix);
    if(File.Exists(sidecar))files.Add(new KeyValuePair<string,string>(sidecar,suffix));
   }
   string destinationStem=System.IO.Path.Combine(folder,stem);
   for(int i=2;files.Any(file=>File.Exists(destinationStem+file.Value)||Directory.Exists(destinationStem+file.Value));i++)
    destinationStem=System.IO.Path.Combine(folder,stem+" ("+i+")");
   string destination=destinationStem+extension;
   bool movingCurrent=current!=null&&SamePath(current.Path,source);
   bool resume=playing;
   if(movingCurrent)media.Close();
   var moved=new List<KeyValuePair<string,string>>();
   try{
    foreach(var file in files){
     string target=destinationStem+file.Value;
     File.Move(file.Key,target);
     moved.Add(new KeyValuePair<string,string>(file.Key,target));
    }
   }catch{
    moved.Reverse();
    foreach(var file in moved)File.Move(file.Value,file.Key);
    if(movingCurrent){PlayCurrent();if(!resume)Pause();}
    throw;
   }
   DateTime added;
   if(prefs.Added.TryGetValue(source,out added)){prefs.Added.Remove(source);prefs.Added[destination]=added;}
   for(int i=0;i<prefs.Favorites.Count;i++)if(SamePath(prefs.Favorites[i],source))prefs.Favorites[i]=destination;
   foreach(Track item in all.Concat(queue).Concat(new[]{track}).Distinct().Where(item=>SamePath(item.Path,source))){
    item.Path=destination;
    item.Section=Storage.Relative(folder);
   }
   if(movingCurrent){PlayCurrent();if(!resume)Pause();}
   Save();
   Refresh();
  }

  void Select(string folder,string artist,bool favorites=false){
   selected=folder;
   artistFilter=artist;
   if(favoritesView!=favorites){
    changingSort=true;
    var sort=C<ComboBox>("Sort");
    if(favorites){
     sort.Items.Add(new ComboBoxItem{Content="Custom order"});
     sort.Items.Add(new ComboBoxItem{Content="Section name"});
    }else{
     sort.Items.RemoveAt(3);
     sort.Items.RemoveAt(2);
    }
    favoritesView=favorites;
    sort.SelectedIndex=favorites?favoritesSort:prefs.Sort;
    changingSort=false;
   }
   Render();
  }
  void UpdateFavoritesCount(){C<TextBlock>("FavoritesCount").Text=all.Count(track=>track.IsFavorite).ToString(CultureInfo.CurrentCulture);}
  void Render(){
   if(all==null)return;
   string query=C<TextBox>("Search").Text.Trim();
   var favorites=new HashSet<string>(prefs.Favorites,StringComparer.OrdinalIgnoreCase);
   foreach(Track track in all)track.IsFavorite=favorites.Contains(track.Path);
   UpdateFavoritesCount();
   IEnumerable<Track> items=all.Where(t=>favoritesView?t.IsFavorite:t.Path.StartsWith(selected+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
   if(artistFilter!=null)items=items.Where(t=>t.Artist.Equals(artistFilter,StringComparison.OrdinalIgnoreCase));
   if(query.Length>0)items=items.Where(t=>(t.Title+" "+t.Artist+" "+t.Section).IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0);
   int sort=favoritesView?favoritesSort:prefs.Sort;
   if(sort==2){
    var byPath=items.ToDictionary(track=>track.Path,StringComparer.OrdinalIgnoreCase);
    visible=prefs.Favorites.Distinct(StringComparer.OrdinalIgnoreCase).Where(byPath.ContainsKey).Select(path=>byPath[path]).ToList();
   }else if(sort==3){
    visible=items.OrderBy(track=>System.IO.Path.GetFileName(track.Section),StringComparer.CurrentCultureIgnoreCase)
     .ThenBy(track=>track.Section,StringComparer.CurrentCultureIgnoreCase)
     .ThenBy(track=>track.Title,StringComparer.CurrentCultureIgnoreCase).ToList();
   }else{
    visible=(sort==1?items.OrderByDescending(t=>t.Added):items.OrderBy(t=>t.Title,StringComparer.CurrentCultureIgnoreCase)).ThenBy(t=>t.Title,StringComparer.CurrentCultureIgnoreCase).ToList();
   }
   C<ListBox>("Tracks").ItemsSource=visible;
   C<StackPanel>("Empty").Visibility=visible.Count==0?Visibility.Visible:Visibility.Collapsed;
   C<TextBlock>("EmptyTitle").Text=favoritesView?"No favorite tracks to show":"Your next favorite belongs here.";
   C<TextBlock>("EmptyHint").Text=favoritesView?"Click a star beside a track to add it to Favorites.":"Import music and organize your favorite tracks.";
   C<TextBlock>("SectionTitle").Text=favoritesView?"Favorites":artistFilter??(selected==Storage.Library?"All music":System.IO.Path.GetFileName(selected));
   C<TextBlock>("Breadcrumb").Text=favoritesView?"YOUR LIBRARY":artistFilter!=null?"ARTIST":Storage.Relative(selected).Replace("\\","  /  ").ToUpperInvariant();
   C<Button>("DeleteSection").IsEnabled=!favoritesView&&selected!=Storage.Library;
   C<Button>("SectionArt").IsEnabled=!favoritesView;
   C<Button>("OpenFolder").IsEnabled=!favoritesView;
   C<Button>("PlaySection").Content=favoritesView?"▶  Play favorites":"▶  Play section";
   C<TextBlock>("SectionInfo").Text=visible.Count+(favoritesView?(visible.Count==1?" favorite track":" favorite tracks"):" tracks · includes subsections");
   SetImage(C<Image>("SectionCover"),favoritesView?null:FindArt(selected,false));
  }
  void Start(List<Track> tracks,int at){queue=new List<Track>(tracks);index=at;PlayCurrent();}
  void PlayCurrent(){if(index<0||index>=queue.Count)return;current=queue[index];if(!File.Exists(current.Path)){SetStatus("This file has moved. Refreshing the library.");Refresh();return;}media.Open(new Uri(current.Path));media.Play();playing=true;C<Button>("PlayPause").Content="❚❚";C<TextBlock>("NowTitle").Text=current.Title;C<TextBlock>("NowArtist").Text=current.Artist;SetImage(C<Image>("NowArt"),FindArt(current.Path,true));tray.Text=("Nocturne · "+current.Title).Substring(0,Math.Min(63,11+current.Title.Length));C<Slider>("Seek").Value=0;}
  void Pause(){if(current==null){if(visible.Count>0)Start(visible,0);return;}if(playing)media.Pause();else media.Play();playing=!playing;C<Button>("PlayPause").Content=playing?"❚❚":"▶";}
  void Advance(int delta){if(queue.Count==0)return;if(delta<0&&media.Position.TotalSeconds>3){media.Position=TimeSpan.Zero;return;}int next=shuffle&&delta>0&&queue.Count>1?(index+1+random.Next(queue.Count-1))%queue.Count:index+delta;if(next<0)next=0;if(next>=queue.Count){if(repeat)next=0;else{media.Stop();playing=false;C<Button>("PlayPause").Content="▶";return;}}index=next;PlayCurrent();}
  void NewFolder(){string name=Prompt("New section","Enter a name for the new section.","");if(string.IsNullOrWhiteSpace(name))return;string path=Storage.Child(Storage.Sections,name);Directory.CreateDirectory(path);Select(path,null);BuildTree();}
  void DeleteSection(string folder){
   if(!Storage.Inside(folder)||string.Equals(folder,Storage.Library,StringComparison.OrdinalIgnoreCase)||!Directory.Exists(folder))return;
   if(MessageBox.Show(window,"Move "+Storage.Relative(folder)+" and all its tracks and subsections to the Recycle Bin?","Delete section",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
   string prefix=folder+System.IO.Path.DirectorySeparatorChar;
   if(current!=null&&current.Path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)){
    media.Close();current=null;playing=false;queue.Clear();index=-1;
    C<Button>("PlayPause").Content="▶";C<TextBlock>("NowTitle").Text="";C<TextBlock>("NowArtist").Text="";C<Image>("NowArt").Source=null;C<Slider>("Seek").Value=0;tray.Text="Nocturne";
   }
   Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(folder,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
   if(selected.Equals(folder,StringComparison.OrdinalIgnoreCase)||selected.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)){selected=System.IO.Path.GetDirectoryName(folder);artistFilter=null;}
   if(current!=null){
    queue.RemoveAll(track=>track.Path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));
    index=queue.IndexOf(current);
   }
   Refresh();
  }
  string Prompt(string title,string subtitle,string initial){var dialog=Dialog(title,460,240);var panel=Panel(dialog);panel.Children.Add(Label(subtitle,13));var box=new TextBox{Text=initial,Margin=new Thickness(0,16,0,16)};panel.Children.Add(box);string result=null;var ok=Button("Continue",()=>{result=box.Text.Trim();dialog.Close();});panel.Children.Add(ok);dialog.Loaded+=(s,e)=>{box.Focus();box.SelectAll();};dialog.ShowDialog();return result;}
  [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
  Window Dialog(string title,double width,double height){var d=new Window{Title=title,Width=width,Height=height,Owner=window,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(8,8,8)),Foreground=Brushes.White,FontFamily=new FontFamily("Segoe UI"),ResizeMode=ResizeMode.CanResize,MinWidth=350,MinHeight=180};d.SourceInitialized+=(s,e)=>{try{int dark=1;DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(d).Handle,20,ref dark,4);}catch{}};foreach(object key in window.Resources.Keys)d.Resources[key]=window.Resources[key];return d;}
  StackPanel Panel(Window d){var p=new StackPanel{Margin=new Thickness(24)};d.Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};return p;}
  static TextBlock Label(string text,double size){return new TextBlock{Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};}
  Button Button(string text,Action action){var b=new Button{Content=text,HorizontalAlignment=HorizontalAlignment.Left};b.Click+=(s,e)=>Safe(action);return b;}
  sealed class DialogOwner : Forms.IWin32Window {
   public IntPtr Handle {get;private set;}
   public DialogOwner(Window owner){Handle=new System.Windows.Interop.WindowInteropHelper(owner).Handle;}
  }
  void Import(){
   var choice=Dialog("Import music",460,260);
   var panel=Panel(choice);
   panel.Children.Add(Label("Copy music into the library. Originals stay where they are.",14));
   panel.Children.Add(Button("Choose audio files",()=>{
    using(var picker=new Forms.OpenFileDialog{Filter="Music|*.mp3;*.m4a;*.wav;*.wma;*.aac",Multiselect=true})
     if(picker.ShowDialog(new DialogOwner(choice))==Forms.DialogResult.OK){choice.Close();CopyImport(picker.FileNames,false);}
   }));
   panel.Children.Add(Button("Choose a folder",()=>{
    string folder=ExplorerFolderPicker.Choose(new DialogOwner(choice).Handle,"Choose a music folder to import",Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    if(folder!=null){choice.Close();CopyFolder(folder);}
   }));
   choice.ShowDialog();
  }
  async void CopyImport(string[] paths,bool unused){string destination=Storage.Library;try{SetStatus("Importing…");await Task.Run(()=>{foreach(string f in paths){string target=System.IO.Path.Combine(destination,System.IO.Path.GetFileName(f));if(f.Equals(target,StringComparison.OrdinalIgnoreCase))continue;File.Copy(f,Storage.Unique(target));}});Refresh();}catch(Exception ex){SetStatus("Import failed: "+ex.Message);}}
  async void CopyFolder(string source){try{source=System.IO.Path.GetFullPath(source);if(Storage.Inside(source))throw new Exception("This folder is already in your library.");if(Storage.Library.StartsWith(source+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Choose a music folder, rather than a parent of the library.");string destination=Storage.Child(Storage.Sections,new DirectoryInfo(source).Name);SetStatus("Copying folder…");await Task.Run(()=>CopyDirectory(source,destination));Select(destination,null);Refresh();}catch(Exception ex){SetStatus("Import failed: "+ex.Message);}}
  static void CopyDirectory(string source,string dest){Directory.CreateDirectory(dest);foreach(string file in Directory.GetFiles(source)){string ext=System.IO.Path.GetExtension(file).ToLowerInvariant();if(Storage.AudioExtensions.Contains(ext)||new[]{".jpg",".png",".jpeg",".json"}.Contains(ext))File.Copy(file,Storage.Unique(System.IO.Path.Combine(dest,System.IO.Path.GetFileName(file))));}foreach(string d in Directory.GetDirectories(source))if((File.GetAttributes(d)&FileAttributes.ReparsePoint)==0)CopyDirectory(d,System.IO.Path.Combine(dest,System.IO.Path.GetFileName(d)));}
  void ShowArtists(){var d=Dialog("Artists",520,560);var p=Panel(d);p.Children.Add(Label("Artists in your library",22));var search=new TextBox{Margin=new Thickness(0,8,0,12)};var list=new ListBox{Background=Brushes.Transparent,BorderThickness=new Thickness(0),Height=340};Action filter=()=>list.ItemsSource=all.Select(t=>t.Artist).Distinct(StringComparer.CurrentCultureIgnoreCase).Where(a=>a.IndexOf(search.Text,StringComparison.CurrentCultureIgnoreCase)>=0).OrderBy(a=>a).ToList();search.TextChanged+=(s,e)=>filter();list.MouseDoubleClick+=(s,e)=>{if(list.SelectedItem!=null){Select(Storage.Library,list.SelectedItem.ToString());d.Close();}};p.Children.Add(search);p.Children.Add(list);p.Children.Add(Button("Show artist",()=>{if(list.SelectedItem!=null){Select(Storage.Library,list.SelectedItem.ToString());d.Close();}}));filter();d.ShowDialog();}
  void ScanMusicFolders(){Directory.CreateDirectory(Storage.Sections);Refresh();}
  void ApplyDisplayName(){window.Title=string.IsNullOrWhiteSpace(prefs.DisplayName)?"Nocturne":prefs.DisplayName;C<TextBlock>("AppName").Text="  "+window.Title;}
  void ShowSettings(){var d=Dialog("Settings",540,565);var p=Panel(d);p.Children.Add(Label("Make it yours",25));p.Children.Add(Label("App display name",12));var appName=new TextBox{Text=prefs.DisplayName,MaxLength=48};p.Children.Add(appName);var trayCheck=new CheckBox{Content="Minimize (−) to the system tray",IsChecked=prefs.Tray};var cover=new CheckBox{Content="Show artwork in the mini-player",IsChecked=prefs.MiniCover};var top=new CheckBox{Content="Keep the mini-player above other windows",IsChecked=prefs.AlwaysOnTop};var sharedArt=new CheckBox{Content="Apply track artwork to its whole section by default",IsChecked=prefs.ApplyTrackArtworkToSection};var sectionCovers=new CheckBox{Content="Cover-only section cards",IsChecked=prefs.CoverOnlySections};p.Children.Add(trayCheck);p.Children.Add(cover);p.Children.Add(top);p.Children.Add(sectionCovers);p.Children.Add(sharedArt);p.Children.Add(Label("Closing with X always quits and stops the music.",12));p.Children.Add(Label("Music library\n"+Storage.Library,13));p.Children.Add(Button("Open music folder",()=>Open(Storage.Library)));var legacyLibrary=new CheckBox{Content="Use the previous Documents\\Music library (restart required)",IsChecked=File.Exists(Storage.LibraryChoice)};p.Children.Add(legacyLibrary);p.Children.Add(Button("Scan music folders",()=>{ScanMusicFolders();d.Close();}));p.Children.Add(Label("MP3, M4A, AAC, WAV and WMA use Windows audio support.\nArtwork supports local files and HTTPS image URLs.",12));p.Children.Add(Button("Save settings",()=>{prefs.DisplayName=string.IsNullOrWhiteSpace(appName.Text)?"Nocturne":appName.Text.Trim();ApplyDisplayName();prefs.Tray=trayCheck.IsChecked==true;prefs.MiniCover=cover.IsChecked==true;prefs.AlwaysOnTop=top.IsChecked==true;prefs.CoverOnlySections=sectionCovers.IsChecked==true;prefs.ApplyTrackArtworkToSection=sharedArt.IsChecked==true;if(legacyLibrary.IsChecked==true)Storage.Atomic(Storage.LibraryChoice,"enabled");else if(File.Exists(Storage.LibraryChoice))File.Delete(Storage.LibraryChoice);Save();ApplyMini();BuildTree();d.Close();}));d.ShowDialog();}
  static string ArtKey(string target){using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(target.ToLowerInvariant()))).Replace("-","");}
  static string OverridePath(string target){return System.IO.Path.Combine(Storage.Data,"Artwork",ArtKey(target)+".jpg");}
  static string FindSectionCover(string folder){
   string custom=OverridePath(folder);
   if(File.Exists(custom))return custom;
   foreach(string name in new[]{"cover.jpg","folder.jpg","cover.png","folder.png"}){
    string path=System.IO.Path.Combine(folder,name);
    if(File.Exists(path))return path;
   }
   try{return Directory.EnumerateFiles(folder,"*.jpg").FirstOrDefault();}
   catch(IOException){return null;}
   catch(UnauthorizedAccessException){return null;}
  }
  static string FindArt(string target,bool track){string custom=OverridePath(target);if(track){string shared=OverridePath(System.IO.Path.GetDirectoryName(target));if(File.Exists(shared)&&(!File.Exists(custom)||File.GetLastWriteTimeUtc(shared)>=File.GetLastWriteTimeUtc(custom)))return shared;}if(File.Exists(custom))return custom;if(track){string stem=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target),System.IO.Path.GetFileNameWithoutExtension(target));foreach(string ext in new[]{".jpg",".png",".jpeg"})if(File.Exists(stem+ext))return stem+ext;string embedded=System.IO.Path.Combine(Storage.Data,"Artwork",ArtKey(target)+".embedded.jpg");if(File.Exists(embedded))return embedded;TryExtractCover(target,embedded);if(File.Exists(embedded))return embedded;target=System.IO.Path.GetDirectoryName(target);}string cover=FindSectionCover(target);if(cover!=null)return cover;try{foreach(string dir in Directory.GetDirectories(target).OrderBy(x=>x)){if(!Storage.Inside(dir)||(File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)continue;string image=FindArt(dir,false);if(image!=null)return image;}}catch{}return null;}
  static void TryExtractCover(string path,string dest){if(!System.IO.Path.GetExtension(path).Equals(".mp3",StringComparison.OrdinalIgnoreCase))return;try{using(var f=File.OpenRead(path)){byte[] h=new byte[10];if(f.Read(h,0,10)!=10||Encoding.ASCII.GetString(h,0,3)!="ID3"||h[3]<3||h[3]>4)return;int size=Synch(h,6);if(size>16*1024*1024)return;byte[] b=new byte[size];if(f.Read(b,0,size)!=size)return;int pos=0;if((h[5]&64)!=0)pos=h[3]==4?Synch(b,0):Big(b,0)+4;while(pos+10<=size){string id=Encoding.ASCII.GetString(b,pos,4);int len=h[3]==4?Synch(b,pos+4):Big(b,pos+4);if(len<=0||pos+10+len>size)break;if(id=="APIC"){int end=pos+10+len;for(int i=pos+11;i+8<end;i++){if((b[i]==255&&b[i+1]==216&&b[i+2]==255)||(b[i]==137&&b[i+1]==80&&b[i+2]==78&&b[i+3]==71)){Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));using(var o=File.Create(dest))o.Write(b,i,end-i);return;}}}pos+=10+len;}}}catch{}}
  static void SetImage(Image image,string path,int decodeWidth=320){try{if(path==null){image.Source=null;return;}var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.UriSource=new Uri(path);b.DecodePixelWidth=decodeWidth;b.EndInit();b.Freeze();image.Source=b;}catch{image.Source=null;}}
  void Artwork(string target,bool track){var d=Dialog(track?"Track artwork":"Section artwork",520,580);var p=Panel(d);p.Children.Add(Label(track?System.IO.Path.GetFileNameWithoutExtension(target):Storage.Relative(target),20));var preview=new Image{Height=180,Margin=new Thickness(0,8,0,12),Stretch=Stretch.Uniform};SetImage(preview,FindArt(target,track));p.Children.Add(preview);var query=new TextBox{Text=track&&current!=null?current.Title:System.IO.Path.GetFileName(target),Margin=new Thickness(0,5,0,10)};p.Children.Add(query);var shared=new CheckBox{Content="Apply to all tracks in this section",IsChecked=prefs.ApplyTrackArtworkToSection};if(track)p.Children.Add(shared);Func<string> artworkTarget=()=>track&&shared.IsChecked==true?System.IO.Path.GetDirectoryName(target):target;var actions=new System.Windows.Controls.Primitives.UniformGrid{Rows=2,Columns=2,Margin=new Thickness(0,0,0,8)};actions.Children.Add(Button("Browse Google Images ↗",()=>Open("https://www.google.com/search?tbm=isch&q="+Uri.EscapeDataString(query.Text+" album artwork"))));actions.Children.Add(Button("Choose a local image…",()=>{var f=new Forms.OpenFileDialog{Filter="Images|*.jpg;*.jpeg;*.png;*.bmp"};if(f.ShowDialog()==Forms.DialogResult.OK){StoreArt(artworkTarget(),f.FileName);SetImage(preview,FindArt(target,track));UpdateArt();}}));actions.Children.Add(Button("Use image URL…",()=>{string url=Prompt("Image URL","Paste a direct HTTPS image address","");if(url!=null)FetchArt(artworkTarget(),url,preview);}));actions.Children.Add(Button("Restore original artwork",()=>{string output=OverridePath(artworkTarget());if(File.Exists(output))File.Delete(output);SetImage(preview,FindArt(target,track));UpdateArt();}));foreach(Button action in actions.Children)action.HorizontalAlignment=HorizontalAlignment.Stretch;p.Children.Add(actions);p.Children.Add(Label("Artwork changes stay in Nocturne; your original audio is preserved.",11));d.ShowDialog();}
  static void StoreArt(string target,string source){var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.UriSource=new Uri(source);b.DecodePixelWidth=1200;b.EndInit();var encoder=new JpegBitmapEncoder{QualityLevel=90};encoder.Frames.Add(BitmapFrame.Create(b));string dest=OverridePath(target);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));using(var f=File.Create(dest))encoder.Save(f);}
  async void FetchArt(string target,string url,Image preview){string temp=System.IO.Path.Combine(Storage.Data,Guid.NewGuid()+".image");try{Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https")throw new Exception("Use a direct HTTPS image URL.");using(var web=new WebClient())await web.DownloadFileTaskAsync(uri,temp);if(new FileInfo(temp).Length>20*1024*1024)throw new Exception("Choose an image smaller than 20 MB.");StoreArt(target,temp);SetImage(preview,OverridePath(target));UpdateArt();}catch(Exception ex){MessageBox.Show(window,ex.Message,"Artwork");}finally{if(File.Exists(temp))File.Delete(temp);}}
  void UpdateArt(){BuildTree();Render();if(current!=null)SetImage(C<Image>("NowArt"),FindArt(current.Path,true));}
 }
 public static class Program {
  [STAThread] public static int Main(string[] args){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;if(args.Contains("--self-test"))return SelfTest();using(var mutex=new Mutex(true,"Local\\NocturneLocal.Player",out created)){if(!created){MessageBox.Show("Nocturne is already running. Check the taskbar or system tray.","Nocturne");return 0;}try{new Player().Run();return 0;}catch(Exception ex){MessageBox.Show(ex.ToString(),"Nocturne could not start");return 1;}}}static bool created;
  static int SelfTest(){try{if(!Storage.Inside(System.IO.Path.Combine(Storage.Library,"A","B")))throw new Exception("Nested library path rejected");if(Storage.Inside(Storage.Library+" Other"))throw new Exception("Sibling library path accepted");bool rejected=false;try{Storage.Child(Storage.Library,"../bad");}catch{rejected=true;}if(!rejected)throw new Exception("Traversal accepted");using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml")){var w=(Window)XamlReader.Load(stream);foreach(string name in new[]{"Folders","SectionsRoot","RefreshLibrary","NewFolder","Import","Tracks","Favorites","FavoritesCount","PlayPause","Seek","Volume","LibraryView","TitleBar","Compact","CloseApp"})if(w.FindName(name)==null)throw new Exception("Missing UI control: "+name);w.Close();}File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),"PASS: library boundaries, nested paths, traversal rejection, XAML and controls");return 0;}catch(Exception ex){File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),ex.ToString());return 1;}}
 }
}


