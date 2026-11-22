using System;
using System.IO;
using System.Runtime.InteropServices;

internal static class ExplorerFolderPicker {
 public static string Choose(IntPtr owner, string title, string initialFolder) {
  var dialog = (IFileDialog)new FileOpenDialog();
  try {
   const uint pickFolders = 0x20, forceFileSystem = 0x40, pathMustExist = 0x800;
   uint options;
   dialog.GetOptions(out options);
   dialog.SetOptions(options | pickFolders | forceFileSystem | pathMustExist);
   dialog.SetTitle(title);
   dialog.SetOkButtonLabel("Select folder");
   if (Directory.Exists(initialFolder)) {
    Guid itemId = typeof(IShellItem).GUID;
    IShellItem initial;
    if (SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref itemId, out initial) == 0) {
     try { dialog.SetFolder(initial); }
     finally { Marshal.ReleaseComObject(initial); }
    }
   }
   int result = dialog.Show(owner);
   if (result == unchecked((int)0x800704C7)) return null;
   Marshal.ThrowExceptionForHR(result);
   IShellItem folder;
   dialog.GetResult(out folder);
   try {
    IntPtr path;
    folder.GetDisplayName(0x80058000, out path);
    try { return Marshal.PtrToStringUni(path); }
    finally { Marshal.FreeCoTaskMem(path); }
   } finally { Marshal.ReleaseComObject(folder); }
  } finally { Marshal.ReleaseComObject(dialog); }
 }

 [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
 static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, out IShellItem item);

 [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
 class FileOpenDialog {}

 // The unused methods preserve the native interface's vtable order.
 [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IFileDialog {
  [PreserveSig] int Show(IntPtr owner);
  void SetFileTypes(uint count, IntPtr filters);
  void SetFileTypeIndex(uint index);
  void GetFileTypeIndex(out uint index);
  void Advise(IntPtr events, out uint cookie);
  void Unadvise(uint cookie);
  void SetOptions(uint options);
  void GetOptions(out uint options);
  void SetDefaultFolder(IShellItem folder);
  void SetFolder(IShellItem folder);
  void GetFolder(out IShellItem folder);
  void GetCurrentSelection(out IShellItem item);
  void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
  void GetFileName(out IntPtr name);
  void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
  void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
  void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
  void GetResult(out IShellItem item);
 }

 [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IShellItem {
  void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
  void GetParent(out IShellItem parent);
  void GetDisplayName(uint format, out IntPtr name);
 }
}
