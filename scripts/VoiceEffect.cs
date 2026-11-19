using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AtkBatteryTray {
    /// <summary>
    /// 实时语音处理链：默认麦克风 -> 变声（共振峰重采样）/ 降噪（噪声门）-> 虚拟声卡。
    /// 使用 winmm（waveIn/waveOut），不依赖任何第三方库，也不需要驱动。
    /// 输出优先送到 Voicemeeter Input / VB-CABLE，配合虚拟麦克风即可在游戏、语音软件里生效。
    /// </summary>
    internal sealed class VoiceEffectEngine : IDisposable {
        internal enum Preset { Off=0,Deep=1,High=2,Cartoon=3 }

        const int SampleRate=48000;
        const int ChannelsOut=2;
        const int BlockSamples=960;                 // 20 ms
        const int BlockBytesIn=BlockSamples*2;      // mono 16-bit
        const int BlockBytesOut=BlockSamples*4;     // stereo 16-bit
        const int InputBuffers=4;
        const int OutputBuffers=6;
        const int RingSize=8192;

        const int WAVE_MAPPER=-1;
        const int CALLBACK_FUNCTION=0x00030000;
        const int WIM_DATA=0x3C0;
        const int WOM_DONE=0x3BB;
        const int WHDR_DONE=0x00000001;

        delegate void WaveInProc(IntPtr hwi,int message,IntPtr instance,IntPtr param1,IntPtr param2);
        delegate void WaveOutProc(IntPtr hwo,int message,IntPtr instance,IntPtr param1,IntPtr param2);

        [StructLayout(LayoutKind.Sequential,Pack=1)]
        struct WAVEFORMATEX {
            internal short FormatTag,Channels;
            internal int SamplesPerSec,AvgBytesPerSec;
            internal short BlockAlign,BitsPerSample,CbSize;
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct WAVEINCAPS {
            internal short Mid,Pid;
            internal int DriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] internal string Name;
            internal int Formats;
            internal short Channels,Reserved;
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct WAVEOUTCAPS {
            internal short Mid,Pid;
            internal int DriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] internal string Name;
            internal int Formats;
            internal short Channels,Reserved;
            internal int Support;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR {
            internal IntPtr Data;
            internal int BufferLength;
            internal int BytesRecorded;
            internal IntPtr User;
            internal int Flags;
            internal int Loops;
            internal IntPtr Next,Reserved;
        }

        [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
        [DllImport("winmm.dll",CharSet=CharSet.Unicode)] static extern int waveInGetDevCapsW(int device,ref WAVEINCAPS caps,int size);
        [DllImport("winmm.dll",CharSet=CharSet.Unicode)] static extern int waveInOpen(out IntPtr handle,int device,ref WAVEFORMATEX format,WaveInProc callback,IntPtr instance,int flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr handle,IntPtr header,int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr handle,IntPtr header,int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr handle,IntPtr header,int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr handle);
        [DllImport("winmm.dll")] static extern int waveInStop(IntPtr handle);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr handle);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr handle);

        [DllImport("winmm.dll")] static extern int waveOutGetNumDevs();
        [DllImport("winmm.dll",CharSet=CharSet.Unicode)] static extern int waveOutGetDevCapsW(int device,ref WAVEOUTCAPS caps,int size);
        [DllImport("winmm.dll",CharSet=CharSet.Unicode)] static extern int waveOutOpen(out IntPtr handle,int device,ref WAVEFORMATEX format,WaveOutProc callback,IntPtr instance,int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr handle,IntPtr header,int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr handle,IntPtr header,int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr handle,IntPtr header,int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr handle);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr handle);

        sealed class Buffer {
            internal IntPtr Data,Header;
            internal int Bytes;
            internal bool Busy;
            internal long Stamp;
        }

        internal static readonly VoiceEffectEngine Shared=new VoiceEffectEngine();

        readonly object sync=new object();
        readonly List<Buffer> inputs=new List<Buffer>(),outputs=new List<Buffer>();
        readonly WaveInProc inputCallback;
        readonly WaveOutProc outputCallback;
        IntPtr inputHandle,outputHandle;
        bool running,stopping;
        int pitchPercent=100;
        volatile bool gateEnabled;
        int gateDb=-42;
        string destination="",error="";
        int processedBlocks,droppedBlocks,writeFailures;

        readonly float[] ring=new float[RingSize];
        int writePos;
        float readPos;
        float gateGain=1f;

        VoiceEffectEngine() {
            inputCallback=new WaveInProc(OnInput);
            outputCallback=new WaveOutProc(OnOutput);
        }

        internal bool Running {get{lock(sync)return running;}}
        internal string Destination {get{lock(sync)return destination;}}
        internal string Error {get{lock(sync)return error;}}
        internal int PitchPercent {get{return pitchPercent;}}
        internal bool GateEnabled {get{return gateEnabled;}}
        internal int GateDb {get{return gateDb;}}
        internal int ProcessedBlocks {get{return processedBlocks;}}
        internal int DroppedBlocks {get{return droppedBlocks;}}
        internal int WriteFailures {get{return writeFailures;}}
        internal Preset CurrentPreset {get{return PresetFor(pitchPercent);}}

        internal static Preset PresetFor(int percent) {
            if(percent==78)return Preset.Deep;
            if(percent==128)return Preset.High;
            if(percent==155)return Preset.Cartoon;
            return Preset.Off;
        }
        internal static int PercentFor(Preset preset) {
            switch(preset){case Preset.Deep:return 78;case Preset.High:return 128;case Preset.Cartoon:return 155;default:return 100;}
        }
        internal static string PresetName(Preset preset) {
            switch(preset){case Preset.Deep:return "低沉";case Preset.High:return "尖锐";case Preset.Cartoon:return "卡通";default:return "原声";}
        }

        internal bool Start(Preset preset,bool gate,int thresholdDb) {
            lock(sync) {
                if(running) {
                    pitchPercent=PercentFor(preset);gateEnabled=gate;gateDb=thresholdDb;
                    return true;
                }
                error="";destination="";processedBlocks=0;droppedBlocks=0;
                pitchPercent=PercentFor(preset);gateEnabled=gate;gateDb=thresholdDb;
                for(int i=0;i<ring.Length;i++)ring[i]=0f;
                writePos=0;readPos=0f;gateGain=gate?0f:1f;
                stopping=false;
            }
            string name="";
            int device=FindVirtualPlayback(out name);
            if(device<0) {
                lock(sync){error="未找到虚拟声卡（Voicemeeter Input / CABLE Input）。变声器需要虚拟声卡才能被其他程序当作麦克风使用。";}
                return false;
            }
            lock(sync){destination=name;}
            if(!Open(device)) {Close();return false;}
            lock(sync){running=true;}
            return true;
        }

        internal void Stop() {
            lock(sync) {
                if(!running&&inputHandle==IntPtr.Zero&&outputHandle==IntPtr.Zero)return;
                stopping=true;running=false;
            }
            Close();
        }

        internal void SetPreset(Preset preset){pitchPercent=PercentFor(preset);}
        internal void SetGate(bool enabled,int thresholdDb){gateEnabled=enabled;if(thresholdDb>-90&&thresholdDb<0)gateDb=thresholdDb;}

        bool Open(int playbackDevice) {
            var formatIn=new WAVEFORMATEX {FormatTag=1,Channels=1,SamplesPerSec=SampleRate,AvgBytesPerSec=SampleRate*2,BlockAlign=2,BitsPerSample=16,CbSize=0};
            int result=waveInOpen(out inputHandle,WAVE_MAPPER,ref formatIn,inputCallback,IntPtr.Zero,CALLBACK_FUNCTION);
            if(result!=0){error="无法打开麦克风（错误 "+result+"）";inputHandle=IntPtr.Zero;return false;}

            var formatOut=new WAVEFORMATEX {FormatTag=1,Channels=ChannelsOut,SamplesPerSec=SampleRate,AvgBytesPerSec=SampleRate*4,BlockAlign=4,BitsPerSample=16,CbSize=0};
            result=waveOutOpen(out outputHandle,playbackDevice,ref formatOut,outputCallback,IntPtr.Zero,CALLBACK_FUNCTION);
            if(result!=0){error="无法打开输出设备（错误 "+result+"）";outputHandle=IntPtr.Zero;return false;}

            for(int i=0;i<InputBuffers;i++) {
                var buffer=CreateBuffer(BlockBytesIn);
                if(buffer==null){error="内存不足";return false;}
                int prepared=waveInPrepareHeader(inputHandle,buffer.Header,Marshal.SizeOf(typeof(WAVEHDR)));
                if(prepared!=0){error="麦克风缓冲初始化失败（"+prepared+"）";return false;}
                inputs.Add(buffer);
            }
            for(int i=0;i<OutputBuffers;i++) {
                var buffer=CreateBuffer(BlockBytesOut);
                if(buffer==null){error="内存不足";return false;}
                int prepared=waveOutPrepareHeader(outputHandle,buffer.Header,Marshal.SizeOf(typeof(WAVEHDR)));
                if(prepared!=0){error="输出缓冲初始化失败（"+prepared+"）";return false;}
                outputs.Add(buffer);
            }
            foreach(var buffer in inputs)waveInAddBuffer(inputHandle,buffer.Header,Marshal.SizeOf(typeof(WAVEHDR)));
            foreach(var buffer in outputs)WriteSilence(buffer);
            result=waveInStart(inputHandle);
            if(result!=0){error="麦克风启动失败（"+result+"）";return false;}
            return true;
        }

        void Close() {
            IntPtr input,output;
            lock(sync) {
                input=inputHandle;output=outputHandle;
                inputHandle=IntPtr.Zero;outputHandle=IntPtr.Zero;
                stopping=true;
            }
            if(input!=IntPtr.Zero) {
                try{waveInStop(input);}catch{}
                try{waveInReset(input);}catch{}
            }
            if(output!=IntPtr.Zero) {
                try{waveOutReset(output);}catch{}
            }
            List<Buffer> inputList,outputList;
            lock(sync){inputList=new List<Buffer>(inputs);outputList=new List<Buffer>(outputs);inputs.Clear();outputs.Clear();}
            if(input!=IntPtr.Zero) {
                foreach(var buffer in inputList){try{waveInUnprepareHeader(input,buffer.Header,Marshal.SizeOf(typeof(WAVEHDR)));}catch{}FreeBuffer(buffer);}
                try{waveInClose(input);}catch{}
            }
            if(output!=IntPtr.Zero) {
                foreach(var buffer in outputList){try{waveOutUnprepareHeader(output,buffer.Header,Marshal.SizeOf(typeof(WAVEHDR)));}catch{}FreeBuffer(buffer);}
                try{waveOutClose(output);}catch{}
            }
            lock(sync){stopping=false;}
        }

        Buffer CreateBuffer(int bytes) {
            var buffer=new Buffer {Bytes=bytes};
            try {
                buffer.Data=Marshal.AllocHGlobal(bytes);
                buffer.Header=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WAVEHDR)));
                var header=new WAVEHDR {Data=buffer.Data,BufferLength=bytes};
                Marshal.StructureToPtr(header,buffer.Header,false);
                Zero(buffer.Data,bytes);
                return buffer;
            } catch {
                FreeBuffer(buffer);
                return null;
            }
        }

        static void FreeBuffer(Buffer buffer) {
            if(buffer==null)return;
            if(buffer.Data!=IntPtr.Zero){try{Marshal.FreeHGlobal(buffer.Data);}catch{}buffer.Data=IntPtr.Zero;}
            if(buffer.Header!=IntPtr.Zero){try{Marshal.FreeHGlobal(buffer.Header);}catch{}buffer.Header=IntPtr.Zero;}
        }

        static void Zero(IntPtr data,int bytes) {
            byte[] zeros=new byte[bytes];
            Marshal.Copy(zeros,0,data,bytes);
        }

        void WriteSilence(Buffer buffer) {
            if(buffer==null||outputHandle==IntPtr.Zero)return;
            Zero(buffer.Data,buffer.Bytes);
            var header=(WAVEHDR)Marshal.PtrToStructure(buffer.Header,typeof(WAVEHDR));
            header.Flags&=~WHDR_DONE;
            Marshal.StructureToPtr(header,buffer.Header,false);
            buffer.Busy=true;
            waveOutWrite(outputHandle,buffer.Header,Marshal.SizeOf(typeof(WAVEHDR)));
        }

        void OnInput(IntPtr handle,int message,IntPtr instance,IntPtr param1,IntPtr param2) {
            if(message!=WIM_DATA)return;
            try { ProcessHeader(param1); }
            catch { }
        }

        void OnOutput(IntPtr handle,int message,IntPtr instance,IntPtr param1,IntPtr param2) {
            if(message!=WOM_DONE)return;
            try { MarkDone(param1); }
            catch { }
        }

        void MarkDone(IntPtr header) {
            lock(sync) {
                foreach(var buffer in outputs) {
                    if(buffer.Header==header){buffer.Busy=false;return;}
                }
            }
        }

        void ProcessHeader(IntPtr header) {
            if(stopping)return;
            Buffer source=null;
            lock(sync) {
                foreach(var buffer in inputs)if(buffer.Header==header){source=buffer;break;}
            }
            if(source==null)return;

            short[] samples=new short[BlockSamples];
            Marshal.Copy(source.Data,samples,0,BlockSamples);
            float pitch=pitchPercent/100f;

            Buffer target=null;
            lock(sync) {
                foreach(var buffer in outputs)if(!buffer.Busy){target=buffer;break;}
                if(target==null) {
                    long oldest=Int64.MaxValue;
                    foreach(var buffer in outputs)if(buffer.Stamp<oldest){oldest=buffer.Stamp;target=buffer;}
                }
                if(target!=null)target.Stamp=DateTime.UtcNow.Ticks;
            }
            if(target==null) {
                droppedBlocks++;
            } else {
                short[] output=new short[BlockSamples*2];
                RenderBlock(samples,pitch,output);
                Marshal.Copy(output,0,target.Data,output.Length);
                lock(sync) {
                    if(outputHandle!=IntPtr.Zero) {
                        var hdr=(WAVEHDR)Marshal.PtrToStructure(target.Header,typeof(WAVEHDR));
                        hdr.Flags&=~WHDR_DONE;
                        Marshal.StructureToPtr(hdr,target.Header,false);
                        target.Busy=true;
                        if(waveOutWrite(outputHandle,target.Header,Marshal.SizeOf(typeof(WAVEHDR)))!=0)writeFailures++;
                    }
                }
                processedBlocks++;
            }

            // 把麦克风缓冲立即还回去，保持连续采集。
            if(!stopping&&inputHandle!=IntPtr.Zero) {
                try{waveInAddBuffer(inputHandle,source.Header,Marshal.SizeOf(typeof(WAVEHDR)));}catch{}
            }
        }

        void RenderBlock(short[] input,float pitch,short[] output) {
            // 写入环形缓冲
            for(int i=0;i<input.Length;i++) {
                ring[writePos]=input[i]/32768f;
                writePos++;
                if(writePos>=RingSize)writePos=0;
            }
            float threshold=(float)Math.Pow(10,gateDb/20.0);
            float rms=0f;
            for(int i=0;i<input.Length;i++){float v=input[i]/32768f;rms+=v*v;}
            rms=(float)Math.Sqrt(rms/Math.Max(1,input.Length));
            float targetGain=!gateEnabled||rms>=threshold?1f:0f;
            float attack=1f-(float)Math.Exp(-1f/(SampleRate*0.004f));
            float release=1f-(float)Math.Exp(-1f/(SampleRate*0.030f));

            for(int n=0;n<BlockSamples;n++) {
                float phase=readPos/(RingSize/2f);
                if(phase>=2f){readPos-=(RingSize/2f)*2f;phase=readPos/(RingSize/2f);}
                float weight2=Math.Abs(phase-1f);
                if(weight2>1f)weight2=1f;
                float weight1=1f-weight2;

                int p1=(int)readPos;
                float f1=readPos-p1;
                int p2=(int)((readPos+RingSize/2f)%RingSize);
                float f2=((readPos+RingSize/2f)%RingSize)-p2;

                float s1=ring[p1]*(1f-f1)+ring[(p1+1)%RingSize]*f1;
                float s2=ring[p2]*(1f-f2)+ring[(p2+1)%RingSize]*f2;
                float sample=s1*weight1+s2*weight2;

                float coefficient=targetGain>gateGain?attack:release;
                gateGain+=(targetGain-gateGain)*coefficient;
                sample*=gateGain;

                int value=(int)(sample*32767f);
                if(value>32767)value=32767;
                if(value<-32768)value=-32768;
                output[n*2]= (short)value;
                output[n*2+1]= (short)value;

                readPos+=pitch;
                if(readPos>=RingSize)readPos-=RingSize;
            }
        }

        internal static string[] PlaybackDeviceNames() {
            var names=new List<string>();
            int count=waveOutGetNumDevs();
            for(int i=0;i<count;i++) {
                var caps=new WAVEOUTCAPS();
                if(waveOutGetDevCapsW(i,ref caps,Marshal.SizeOf(typeof(WAVEOUTCAPS)))==0)names.Add(caps.Name??"");
            }
            return names.ToArray();
        }

        static int FindVirtualPlayback(out string name) {
            string[] wanted={"voicemeeter input","cable input","voicemeeter aux input"};
            int count=waveOutGetNumDevs();
            for(int w=0;w<wanted.Length;w++) {
                for(int i=0;i<count;i++) {
                    var caps=new WAVEOUTCAPS();
                    if(waveOutGetDevCapsW(i,ref caps,Marshal.SizeOf(typeof(WAVEOUTCAPS)))!=0)continue;
                    string deviceName=caps.Name??"";
                    if(deviceName.ToLowerInvariant().Contains(wanted[w])){name=deviceName;return i;}
                }
            }
            name="";
            return -1;
        }

        public void Dispose() {
            try{Stop();}catch{}
        }
    }
}



