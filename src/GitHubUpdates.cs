using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Nocturne {
 public sealed class ReleaseUpdate {
  public Version Version;
  public string Tag, Url, Digest;
  public long Size;
 }
 public static class GitHubUpdates {
  public const string Repository="Dracylfrr/Nocturne_Music";
  public static Version ParseVersion(string tag){
   Version version;
   if(string.IsNullOrWhiteSpace(tag)||!Version.TryParse(tag.TrimStart('v','V'),out version))throw new InvalidDataException("The release has an invalid version.");
   return new Version(version.Major,version.Minor,Math.Max(0,version.Build),Math.Max(0,version.Revision));
  }
  public static ReleaseUpdate ReadRelease(string json,Version installed){
   var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
   if((bool)data["draft"]||(bool)data["prerelease"])return null;
   string tag=(string)data["tag_name"];
   Version version=ParseVersion(tag);
   if(version<=ParseVersion(installed.ToString()))return null;
   var assets=((IEnumerable)data["assets"]).Cast<Dictionary<string,object>>();
   var asset=assets.SingleOrDefault(item=>(string)item["name"]=="Nocturne.exe");
   if(asset==null)throw new InvalidDataException("The release installer is not available yet.");
   string url=(string)asset["browser_download_url"];
   if(!url.StartsWith("https://github.com/"+Repository+"/releases/download/",StringComparison.Ordinal))throw new InvalidDataException("The installer URL is outside the release repository.");
   object digest;
   if(!asset.TryGetValue("digest",out digest)||digest==null||!System.Text.RegularExpressions.Regex.IsMatch(digest.ToString(),@"^sha256:[a-fA-F0-9]{64}$"))throw new InvalidDataException("The release has no SHA-256 verification digest.");
   long size=Convert.ToInt64(asset["size"]);
   if(size<=0||size>100*1024*1024)throw new InvalidDataException("Unexpected installer size.");
   return new ReleaseUpdate{Version=version,Tag=tag,Url=url,Digest=digest.ToString().Substring(7),Size=size};
  }
  sealed class UpdateClient : WebClient {
   protected override WebRequest GetWebRequest(Uri address){
    var request=base.GetWebRequest(address);
    request.Timeout=30000;
    var http=request as HttpWebRequest;
    if(http!=null)http.ReadWriteTimeout=120000;
    return request;
   }
  }
  static WebClient Client(){
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   var client=new UpdateClient();
   client.Headers[HttpRequestHeader.UserAgent]="Nocturne-Local-Updater";
   client.Headers[HttpRequestHeader.Accept]="application/vnd.github+json";
   return client;
  }
  public static async Task<ReleaseUpdate> CheckAsync(Version installed){
   using(var client=Client())return ReadRelease(await client.DownloadStringTaskAsync("https://api.github.com/repos/"+Repository+"/releases/latest"),installed);
  }
  public static void VerifyFile(string path,ReleaseUpdate release){
   if(new FileInfo(path).Length!=release.Size)throw new InvalidDataException("The installer download is incomplete.");
   string hash;
   using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
   if(!hash.Equals(release.Digest,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The installer failed SHA-256 verification.");
   if(AssemblyName.GetAssemblyName(path).Version!=release.Version)throw new InvalidDataException("The installer version does not match the release.");
  }
  public static async Task<string> DownloadAsync(ReleaseUpdate release){
   string folder=Path.Combine(Path.GetTempPath(),"NocturneLocal-update-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(folder);
   string path=Path.Combine(folder,"Nocturne.exe");
   try{
    using(var client=Client())await client.DownloadFileTaskAsync(release.Url,path);
    VerifyFile(path,release);
    return path;
   }catch{if(File.Exists(path))File.Delete(path);throw;}
  }
 }
}
