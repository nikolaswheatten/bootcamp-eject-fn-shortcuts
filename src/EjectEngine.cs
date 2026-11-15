using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
public sealed class EjectEngine : IDisposable {
 [StructLayout(LayoutKind.Explicit,Size=40)] struct INPUT {
  [FieldOffset(0)] public uint type;
  [FieldOffset(8)] public ushort vk;
  [FieldOffset(12)] public uint flags;
 }
 [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint n,INPUT[] inputs,int size);
 readonly object gate=new object();
 bool held,disposed;
 long next;
 readonly int delay,period;
 readonly Timer timer;
 public Task Worker {get;private set;}
 public string Error {get;private set;}
 public int Presses {get;private set;}
 public int Releases {get;private set;}
 public bool IsHeld {get{lock(gate)return held;}}
 public EjectEngine(int d,int s){if(IntPtr.Size!=8)throw new Exception("x64 required");delay=(Math.Max(0,Math.Min(3,d))+1)*250;period=(int)Math.Round(1000/(2.5+Math.Max(0,Math.Min(31,s))*27.5/31));timer=new Timer(Tick,null,10,10);}
 static void Send(bool down){var i=new INPUT{type=1,vk=0x2e,flags=down?1u:3u};if(SendInput(1,new[]{i},40)!=1)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
 void State(bool down){lock(gate){if(disposed || held==down)return;held=down;if(down){Presses++;next=Environment.TickCount64+delay;}else Releases++;try{Send(down);}catch(Exception e){Error=e.Message;held=false;try{Send(false);}catch{}}}}
 void Tick(object unused){lock(gate){if(disposed || !held || Environment.TickCount64<next)return;try{Send(true);next=Environment.TickCount64+period;}catch(Exception e){Error=e.Message;held=false;try{Send(false);}catch{}}}}
 public void Start(Stream s,Action<bool,int,bool> observe){Worker=Task.Run(()=>{try{var reader=new EjectPcapReader{ObserveKeyboard=observe};reader.Run(s,State);}catch(Exception e){Error=e.Message;}finally{State(false);}});}
 public void Dispose(){lock(gate){if(disposed)return;disposed=true;timer.Dispose();if(held){held=false;try{Send(false);}catch{}}}}
 public static void ReleaseDelete(){Send(false);}
}
