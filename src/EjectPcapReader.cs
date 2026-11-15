using System;
using System.IO;
using System.Collections.Generic;
public sealed class EjectPcapReader {
 public Action<bool,int,bool> ObserveKeyboard {get;set;}
 public int Bus {get;private set;} = -1;
 public int Address {get;private set;} = -1;
 public int Endpoint {get;private set;} = -1;
 public bool Held {get;private set;}
 public int Presses {get;private set;}
 public int Releases {get;private set;}
 static ushort U16(byte[] b,int i){return (ushort)(b[i]|b[i+1]<<8);}
 static uint U32(byte[] b,int i){return (uint)(b[i]|b[i+1]<<8|b[i+2]<<16|b[i+3]<<24);}
 static byte[] Read(Stream s,int n) {var b=new byte[n];int o=0;while(o<n){int r=s.Read(b,o,n-o);if(r==0){if(o==0)return null;throw new InvalidDataException("Partial record");}o+=r;}return b;}
 public void Run(Stream stream,Action<bool> emit) {
  try {
   var head=Read(stream,24);
   if(head==null || U32(head,0)!=0xa1b2c3d4 || U32(head,20)!=249)throw new InvalidDataException("Expected little-endian USBPcap");
   for(;;){
    var rh=Read(stream,16);if(rh==null)break;
    uint size=U32(rh,8);if(size<27 || size>1048576)throw new InvalidDataException("Record size");
    var b=Read(stream,(int)size);if(b==null)throw new InvalidDataException("Missing record");
    try {
     int h=U16(b,0);if(h<27 || h>b.Length)throw new InvalidDataException("Header size");
     if(U32(b,10)!=0)continue;
     int bus=U16(b,17),address=U16(b,19),ep=b[21],kind=b[22],len=b.Length-h;
     // Injected USB device descriptor verifies identity before interpreting any input.
     if(kind==2 && len==18 && b[h]==18 && b[h+1]==1 && U16(b,h+8)==0x05ac && U16(b,h+10)==0x0253){
      Bus=bus;Address=address;continue;
     }
     if(bus!=Bus || address!=Address)continue;
     // Obtain interrupt endpoint from interface 0 of configuration descriptor.
     if(kind==2 && len>=9 && b[h]==9 && b[h+1]==2){
      int iface=-1;
      for(int q=h;q+2<=b.Length;){int n=b[q];if(n<2 || q+n>b.Length)break;
       if(b[q+1]==4 && n>=9)iface=b[q+2];
       if(b[q+1]==5 && n>=7 && iface==0 && (b[q+2]&128)!=0 && (b[q+3]&3)==3)Endpoint=b[q+2];
       q+=n;
      }
      continue;
     }
     if(kind!=1 || b[16]!=1 || ep!=Endpoint || len!=10 || U32(b,23)!=10 || b[h]!=1)continue;
     bool hasKey=false;
     for(int key=3;key<=8;key++)hasKey|=b[h+key]!=0;
     ObserveKeyboard?.Invoke((b[h+9]&2)!=0,b[h+1],hasKey||(b[h+9]&1)!=0);
     bool down=(b[h+9]&1)!=0;
     if(down!=Held){Held=down;if(down)Presses++;else Releases++;emit(down);}
    }finally{Array.Clear(b,0,b.Length);}
   }
  }finally{if(Held){Held=false;emit(false);}}
 }
}
