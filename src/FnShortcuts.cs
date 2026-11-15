using System;
public sealed class FnShortcuts : IDisposable {
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit,Size=40)]
 struct Input {
  [System.Runtime.InteropServices.FieldOffset(0)] public uint Type;
  [System.Runtime.InteropServices.FieldOffset(8)] public ushort Key;
  [System.Runtime.InteropServices.FieldOffset(10)] public ushort Scan;
  [System.Runtime.InteropServices.FieldOffset(12)] public uint Flags;
 }
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
 [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)]
 static extern uint SendInput(uint count,Input[] input,int size);
 const uint Quit=0x12;
 delegate IntPtr HookProc(int code,IntPtr wParam,IntPtr data);
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
 struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X,Y; public uint Private; }
 [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,HookProc callback,IntPtr module,uint thread);
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr data);
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetMessage(out Message message,IntPtr window,uint first,uint last);
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PeekMessage(out Message message,IntPtr window,uint first,uint last,uint remove);
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread,uint message,UIntPtr wParam,IntPtr lParam);
 [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
 readonly HookProc callback;
 readonly System.Threading.ManualResetEventSlim hookReady = new System.Threading.ManualResetEventSlim(false);
 System.Threading.Thread hookThread;
 uint hookThreadId;
 int hookError;
 readonly bool[] blockedEditKeys=new bool[256];
 readonly bool[] physicalEditKeysDown=new bool[256];
 volatile bool fnHeld;
 volatile bool disposed;
 IntPtr chordWindow;
 public string Error {get;private set;}
 bool chord, contaminated;
  public FnShortcuts(){callback=Keyboard;}
 public void StartHook(){
  hookThread=new System.Threading.Thread(Pump){IsBackground=true};
  hookThread.Start();
  if(!hookReady.Wait(2000))throw new InvalidOperationException("Keyboard hook timeout");
  if(hookError!=0)throw new System.ComponentModel.Win32Exception(hookError);
 }
 void Pump(){
  hookThreadId=GetCurrentThreadId();
  Message message;
  PeekMessage(out message,IntPtr.Zero,0,0,0);
  IntPtr hook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
  if(hook==IntPtr.Zero){
   hookError=System.Runtime.InteropServices.Marshal.GetLastWin32Error();
   if(hookError==0)hookError=1;
   hookReady.Set();
   return;
  }
  hookReady.Set();
  try{while(GetMessage(out message,IntPtr.Zero,0,0)>0){}}
  finally{UnhookWindowsHookEx(hook);}
 }
 static bool PhysicalModifierActive(){
  return ShiftDown()||OtherModifierDown();
 }
 static bool ShiftDown(){return GetAsyncKeyState(0x10)<0;}
 static bool OtherModifierDown(){
  return GetAsyncKeyState(0x11)<0||GetAsyncKeyState(0x12)<0||
         GetAsyncKeyState(0x5B)<0||GetAsyncKeyState(0x5C)<0;
 }
 IntPtr Keyboard(int code,IntPtr wParam,IntPtr data){
  if(code>=0){
   int scan=System.Runtime.InteropServices.Marshal.ReadInt32(data,4);
   int flags=System.Runtime.InteropServices.Marshal.ReadInt32(data,8);
   string shortcut=EditShortcut(scan);
   if(shortcut!=null && (flags&0x11)==0){
    if((flags&0x80)!=0){
     physicalEditKeysDown[scan]=false;
     if(blockedEditKeys[scan]){blockedEditKeys[scan]=false;return new IntPtr(1);}
    }else{
     bool first=!physicalEditKeysDown[scan];
     physicalEditKeysDown[scan]=true;
     if(blockedEditKeys[scan])return new IntPtr(1);
     IntPtr window=GetForegroundWindow();
     bool shifted=ShiftDown();
     if(first && !disposed && fnHeld && window!=IntPtr.Zero && !OtherModifierDown() && AcceptShift(scan,shifted)){
      blockedEditKeys[scan]=true;
      System.Threading.ThreadPool.QueueUserWorkItem(_=>EmitEdit(window,scan,shortcut,shifted));
      return new IntPtr(1);
     }
    }
   }
  }
  return CallNextHookEx(IntPtr.Zero,code,wParam,data);
 }
 public void Dispose(){
  disposed=true;
  fnHeld=false;
  if(hookThread!=null && hookThread.IsAlive){
   PostThreadMessage(hookThreadId,Quit,UIntPtr.Zero,IntPtr.Zero);
   hookThread.Join(2000);
  }
 }
 static Input Key(ushort key,bool up=false){return new Input{Type=1,Key=key,Flags=up?2u:0u};}
 static bool AcceptShift(int scan,bool shifted){return !shifted || scan==0x14 || scan==0x0F || scan==0x2F;}
 void EmitEdit(IntPtr window,int scan,string shortcut,bool shifted){
  if(OtherModifierDown())return;
  bool liveShift=ShiftDown();
  if(!shifted && liveShift)return;
  string label=shifted?shortcut.Insert(3,"Shift+"):shortcut;
  if(shifted && !liveShift)
   Emit(window,label,Key(0xA2),Key(0xA0),ScanKey(scan),ScanKey(scan,true),Key(0xA0,true),Key(0xA2,true));
  else
   Emit(window,label,Key(0xA2),ScanKey(scan),ScanKey(scan,true),Key(0xA2,true));
 }
 static string EditShortcut(int scan){
  switch(scan){
   case 0x1E: return "Fn+A";
   case 0x26: return "Fn+L";
   case 0x0F: return "Fn+Tab";
   case 0x13: return "Fn+R";
   case 0x15: return "Fn+Y";
   case 0x18: return "Fn+O";
   case 0x31: return "Fn+N";
   case 0x2C: return "Fn+Z";
   case 0x1F: return "Fn+S";
   case 0x21: return "Fn+F";
   case 0x14: return "Fn+T";
   case 0x11: return "Fn+W";
   case 0x2D: return "Fn+X";
   case 0x2E: return "Fn+C";
   case 0x2F: return "Fn+V";
   default: return null;
  }
 }
 static Input ScanKey(int scan,bool up=false){
  return new Input{Type=1,Scan=(ushort)scan,Flags=up?10u:8u};
 }
 void Emit(IntPtr window,string label,params Input[] keys){
  if(disposed || window==IntPtr.Zero || GetForegroundWindow()!=window)return;
  uint sent=SendInput((uint)keys.Length,keys,40);
  if(sent!=keys.Length){
   // The second half releases keys in reverse order.
   int half=keys.Length/2;
   int firstRelease=Math.Max((int)sent,keys.Length-Math.Min((int)sent,half));
   if(firstRelease<keys.Length){
    var releases=new Input[keys.Length-firstRelease];
    Array.Copy(keys,firstRelease,releases,0,releases.Length);
    SendInput((uint)releases.Length,releases,40);
   }
   Error=label+" accepted "+sent+"/"+keys.Length+" key events";
  }
 }
 public void Observe(bool fn,int modifiers,bool hasKey){
  if(disposed)return;
  fnHeld=fn;
  bool shift=(modifiers&2)!=0;
  IntPtr window=GetForegroundWindow();
  bool clutter=hasKey||(modifiers&~2)!=0;
  if(chord && (clutter||window!=chordWindow))contaminated=true;
  if(fn && shift && !chord){chord=true;chordWindow=window;contaminated=clutter;}
  if(chord && !fn && !shift){
   if(!contaminated){
    IntPtr destination=chordWindow;
    System.Threading.ThreadPool.QueueUserWorkItem(_=>{
     for(int i=0;i<20&&!disposed&&GetAsyncKeyState(0xA0)<0;i++)
      System.Threading.Thread.Sleep(5);
     if(!PhysicalModifierActive())
      Emit(destination,"Fn+LeftShift",Key(0x5B),Key(0x20),Key(0x20,true),Key(0x5B,true));
    });
   }
   chord=false;
   contaminated=false;
  }
 }
}
