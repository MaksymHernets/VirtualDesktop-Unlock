using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Threading;
using System.Xml;
using System.Globalization;
using VirtualDesktop.Unlock;

namespace VirtualDesktop.LibraryOverrides
{
    public static class BitrateRuntime
    {
        private static volatile BitrateConfiguration current = new BitrateConfiguration();
        private static readonly Dictionary<long, int> stock = new Dictionary<long, int>();
        private static Assembly streamer;
        private static Action<string> log;
        private static DispatcherTimer timer;
        private static string revision;
        private static string failedRevision;
        private static bool installed;
        private static FieldInfo activeCodecField;
        private static FieldInfo sharedSettingsField;
        private static string lastDiagnostics;
        private static DateTime nextDiagnostics;
        private static Func<object,IntPtr> nativeConfiguration;
        private static Type nativeWrapperType;
        private static int nativeAverageOffset;
        private static int nativeMaximumOffset;
        private static string ConfigurationPath { get { return Environment.GetEnvironmentVariable("VD_BITRATE_CONFIG") ?? BitrateConfiguration.DefaultPath; } }
        private static string StatusPath { get { return Environment.GetEnvironmentVariable("VD_BITRATE_STATUS") ?? Path.Combine(Path.GetDirectoryName(ConfigurationPath), "BitrateStatus.xml"); } }

        public static void Install(Assembly assembly, Action<string> logger)
        {
            if (installed) return;
            if (assembly.GetName().Version != new Version(1,34,23,0))
            {
                log=logger;
                try { current=BitrateConfiguration.Read(ConfigurationPath);revision=BitrateConfiguration.Revision(ConfigurationPath); }
                catch { current=new BitrateConfiguration();revision=null; }
                string reason="Bitrate overrides are unavailable on Streamer "+assembly.GetName().Version+"; game-library additions are supported. Stock bitrate behavior retained.";
                WriteStatus(false,reason,true);logger(reason);return;
            }
            if (IntPtr.Size != 8) throw new NotSupportedException("Bitrate overrides require x64 Streamer 1.34.23.0.");
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(assembly.Location))).Replace("-", "") != "FEAD548C57DF539006E6C97A7A29D46FDE172F756F40D65DBDB77ABAAFA3AEAD")
                    throw new NotSupportedException("Bitrate override executable hash does not match the verified Streamer.");
            streamer = assembly; log = logger;
            InitializeNativeDiagnostics();
            var sharedType=assembly.GetType("VirtualDesktop.Mobile.SharedStreamerSettings",true);
            activeCodecField=sharedType.GetField("_activeCodec",BindingFlags.Instance|BindingFlags.NonPublic);
            for(var baseType=sharedType;baseType!=null;baseType=baseType.BaseType)
            {
                sharedSettingsField=baseType.GetField("<Default>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(sharedSettingsField!=null)break;
            }
            var method = assembly.ManifestModule.ResolveMethod(0x060212B6) as MethodInfo;
            var parameters = method.GetParameters();
            if (!method.IsStatic || method.ReturnType != typeof(int) || parameters.Length != 3 || parameters[0].ParameterType.FullName != "Xenko.VR.HmdType" || parameters[1].ParameterType.FullName != "VirtualDesktop.Net.VideoCodec" || parameters[2].ParameterType != typeof(bool))
                throw new MissingMethodException("Unexpected bitrate calculation signature.");
            foreach (object headset in Enum.GetValues(parameters[0].ParameterType))
                foreach (object codec in Enum.GetValues(parameters[1].ParameterType))
                    foreach (bool foveated in new[] { false, true })
                        stock[Key(Convert.ToInt32(headset),Convert.ToInt32(codec),foveated)] = (int)method.Invoke(null,new[] { headset,codec,(object)foveated });
            // Install once, before client streaming starts. Runtime toggles only swap an immutable
            // configuration reference; they never rewrite running machine code again.
            var replacement = typeof(BitrateRuntime).GetMethod("Dispatch", BindingFlags.Static | BindingFlags.NonPublic);
            RuntimeHelpers.PrepareMethod(method.MethodHandle); RuntimeHelpers.PrepareMethod(replacement.MethodHandle);
            IntPtr address = method.MethodHandle.GetFunctionPointer();
            byte[] prologue = new byte[13]; Marshal.Copy(address,prologue,0,prologue.Length);
            byte[] expected = {0x55,0x57,0x56,0x53,0x48,0x83,0xec,0x28,0x48,0x8d,0x6c,0x24,0x40};
            for (int i=0;i<expected.Length;i++) if (prologue[i] != expected[i]) throw new NotSupportedException("Unexpected JIT entry; bitrate hook was not applied.");
            byte[] jump = new byte[12]; jump[0]=0x48; jump[1]=0xb8;
            Array.Copy(BitConverter.GetBytes(replacement.MethodHandle.GetFunctionPointer().ToInt64()),0,jump,2,8); jump[10]=0xff; jump[11]=0xe0;
            uint old;
            if (!VirtualProtect(address,(UIntPtr)jump.Length,0x40,out old)) throw new System.ComponentModel.Win32Exception();
            try { Marshal.Copy(jump,0,address,jump.Length); FlushInstructionCache(Process.GetCurrentProcess().Handle,address,(UIntPtr)jump.Length); }
            finally { uint ignored; VirtualProtect(address,(UIntPtr)jump.Length,old,out ignored); }
            installed = true;
            log("Bitrate hook installed; stock fallback captured; profile updates are atomic.");
            RefreshNow(false);
            if (System.Windows.Application.Current != null)
            {
                timer = new DispatcherTimer(DispatcherPriority.Normal);
                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Tick += delegate { RefreshNow(true); ReportDiagnostics(); };
                timer.Start();
            }
        }

        private static long Key(int headset, int codec, bool foveated) { return ((long)(uint)headset << 32) | ((long)(uint)codec << 1) | (foveated ? 1L : 0L); }
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static int Dispatch(int headset, int codec, bool foveated)
        {
            int baseline;
            if (!stock.TryGetValue(Key(headset,codec,foveated),out baseline)) baseline=40000000;
            var configuration=current;
            // Streamer uses its HEVC calculation for the shared H264/HEVC encoder target.
            // Select the H264 profile when ordinary H264 is actually active.
            int profileCodec=codec;
            if(configuration.Enabled && codec==2)
            {
                try { if(Convert.ToInt32(activeCodecField.GetValue(sharedSettingsField.GetValue(null)))==1)profileCodec=1; }
                catch { }
            }
            return configuration.Limit(headset,profileCodec,baseline);
        }

        public static void RefreshNow(bool notify)
        {
            if (!installed) return;
            string attemptedRevision = null;
            try
            {
                string nextRevision = BitrateConfiguration.Revision(ConfigurationPath);
                if (nextRevision == revision || nextRevision == failedRevision) return;
                attemptedRevision = nextRevision;
                var next = BitrateConfiguration.Read(ConfigurationPath);
                current = next;
                revision = nextRevision;
                failedRevision = null;
                if (notify) NotifyTargets();
                WriteStatus(true,null);
                log("Bitrate profiles applied; enabled="+next.Enabled+"; revision="+revision+"; Quest3 ceilings="+Dispatch(1000,1,false)+","+Dispatch(1000,2,false)+","+Dispatch(1000,10,false));
            }
            catch (Exception e)
            {
                current = new BitrateConfiguration();
                if (notify) { try { NotifyTargets(); } catch { } }
                if(e is XmlException || e is InvalidDataException)failedRevision=attemptedRevision;
                revision = null;
                WriteStatus(false,e.GetBaseException().Message);
                log("Bitrate update failed; stock ceilings restored: "+e.GetBaseException().Message);
            }
        }
        private static void NotifyTargets()
        {
            var mobileType=streamer.GetType("VirtualDesktop.Mobile.SharedMobileSettings",true);
            var mobile=mobileType.BaseType.GetProperty("Default",BindingFlags.Static|BindingFlags.Public|BindingFlags.FlattenHierarchy).GetValue(null,null);
            mobileType.GetMethod("UpdateVRBitrates",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(mobile,null);
            var type=streamer.GetType("VirtualDesktop.Mobile.SharedUserSettings",true);
            var instance=type.BaseType.GetProperty("Default",BindingFlags.Static|BindingFlags.Public|BindingFlags.FlattenHierarchy).GetValue(null,null);
            type.GetMethod("OnVRBitrateLimitChanged",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(instance,new object[] { EventArgs.Empty });
            // OculusStreamer does not subscribe to VRBitrateLimitChanged. Its existing
            // forwarding routine must also refresh any already-created encoder wrappers.
            var encoderOwner=FindEncoderOwner(mobile);
            if(encoderOwner!=null)streamer.ManifestModule.ResolveMethod(0x0600041D).Invoke(encoderOwner,null);
        }
        private static object FindEncoderOwner(object mobile)
        {
            var handlers=mobile.GetType().GetField("HmdTypeChanged",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(mobile) as Delegate;
            if(handlers!=null)
                foreach(var handler in handlers.GetInvocationList())
                    if(handler.Target!=null && streamer.GetType("VirtualDesktop.Streamer.OculusStreamer",true).IsInstanceOfType(handler.Target))return handler.Target;
            return null;
        }
        private static string EncoderDiagnostics(object mobile)
        {
            var owner=FindEncoderOwner(mobile);
            if(owner==null)return "none";
            var values=new List<string>();
            foreach(int token in new[]{0x0400028F,0x04000291})
            {
                var wrapper=streamer.ManifestModule.ResolveField(token).GetValue(owner);
                if(wrapper==null){values.Add("none");continue;}
                values.Add(streamer.ManifestModule.ResolveField(0x04014DAB).GetValue(wrapper)+","+
                    streamer.ManifestModule.ResolveField(0x04014DAC).GetValue(wrapper)+","+
                    streamer.ManifestModule.ResolveField(0x04014DAD).GetValue(wrapper)+" (NVENC "+ReadNativeRateControl(wrapper)+")");
            }
            return String.Join("|",values.ToArray());
        }
        private static void InitializeNativeDiagnostics()
        {
            var pointer=streamer.ManifestModule.ResolveField(0x04014F7B);
            nativeWrapperType=pointer.DeclaringType;
            var getter=new DynamicMethod("ReadNvencConfigurationPointer",typeof(IntPtr),new[]{typeof(object)},typeof(BitrateRuntime),true);
            var il=getter.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,nativeWrapperType);il.Emit(OpCodes.Ldfld,pointer);il.Emit(OpCodes.Conv_I);il.Emit(OpCodes.Ret);
            nativeConfiguration=(Func<object,IntPtr>)getter.CreateDelegate(typeof(Func<object,IntPtr>));
            var rate=streamer.ManifestModule.ResolveField(0x04014FA7);
            nativeAverageOffset=FieldOffset(rate)+FieldOffset(streamer.ManifestModule.ResolveField(0x04015127));
            nativeMaximumOffset=FieldOffset(rate)+FieldOffset(streamer.ManifestModule.ResolveField(0x04015128));
        }
        private static int FieldOffset(FieldInfo field)
        {
            // Obfuscated structs contain duplicate field names, so Marshal.OffsetOf(name)
            // is ambiguous. Compute offsets using the verified field metadata directly.
            var method=new DynamicMethod("VerifiedFieldOffset",typeof(int),Type.EmptyTypes,typeof(BitrateRuntime),true);
            var il=method.GetILGenerator();var local=il.DeclareLocal(field.DeclaringType);
            il.Emit(OpCodes.Ldloca,local);il.Emit(OpCodes.Ldflda,field);il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ldloca,local);il.Emit(OpCodes.Conv_I);il.Emit(OpCodes.Sub);il.Emit(OpCodes.Conv_I4);il.Emit(OpCodes.Ret);
            return ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();
        }
        public static string ReadNativeRateControl(object wrapper)
        {
            if(!nativeWrapperType.IsInstanceOfType(wrapper))return "not applicable";
            IntPtr pointer=nativeConfiguration(wrapper);
            if(pointer==IntPtr.Zero)return "not initialized";
            var average=new byte[4];var maximum=new byte[4];UIntPtr read;
            // Kernel-validated reads fail safely if an encoder has just been disposed.
            if(!ReadProcessMemory(Process.GetCurrentProcess().Handle,IntPtr.Add(pointer,nativeAverageOffset),average,(UIntPtr)4,out read) || read.ToUInt64()!=4 ||
               !ReadProcessMemory(Process.GetCurrentProcess().Handle,IntPtr.Add(pointer,nativeMaximumOffset),maximum,(UIntPtr)4,out read) || read.ToUInt64()!=4)return "unavailable";
            return "average="+BitConverter.ToInt32(average,0)+",maximum="+BitConverter.ToInt32(maximum,0);
        }
        // Read the real settings, rather than treating the hook's configured ceiling as
        // evidence that a connected encoder actually received that target.
        public static string CaptureDiagnostics()
        {
            var mobile=Settings("SharedMobileSettings");
            var user=Settings("SharedUserSettings");
            var shared=Settings("SharedStreamerSettings");
            return "headset="+Convert.ToInt32(Value(mobile,"HmdType"))+
                "; codec="+Value(shared,"ActiveCodec")+
                "; source="+Value(shared,"StreamingSource")+
                "; autoAdjust="+Value(shared,"AutoAdjustBitrate")+
                "; slider="+Convert.ToSingle(Value(user,"VRBitrateLimit")).ToString("R",CultureInfo.InvariantCulture)+
                "; measuredBandwidth="+Value(mobile,"MeasuredBandwidth")+
                "; spacewarp="+Value(mobile,"UseSpacewarp")+
                "; foveated="+Value(mobile,"FoveatedStreaming")+
                "; targets="+Value(mobile,"VRBitrate")+","+Value(mobile,"H264PlusVRBitrate")+","+Value(mobile,"AV1VRBitrate")+
                "; encoderTargets="+EncoderDiagnostics(mobile);
        }
        private static object Settings(string name)
        {
            var type=streamer.GetType("VirtualDesktop.Mobile."+name,true);
            return type.BaseType.GetProperty("Default",BindingFlags.Static|BindingFlags.Public|BindingFlags.FlattenHierarchy).GetValue(null,null);
        }
        private static object Value(object instance,string name)
        {
            return instance.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public).GetValue(instance,null);
        }
        private static void ReportDiagnostics()
        {
            if(DateTime.UtcNow<nextDiagnostics)return;
            nextDiagnostics=DateTime.UtcNow.AddSeconds(5);
            try
            {
                string snapshot=CaptureDiagnostics();
                if(snapshot!=lastDiagnostics){log("Bitrate live settings: "+snapshot);lastDiagnostics=snapshot;}
            }
            catch(Exception e)
            {
                string error="unavailable: "+e.GetBaseException().Message;
                if(error!=lastDiagnostics){log("Bitrate live settings "+error);lastDiagnostics=error;}
            }
        }
        private static void WriteStatus(bool success, string error, bool unsupported=false)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));
                string temp=StatusPath+".tmp";
                using(var writer=XmlWriter.Create(temp,new XmlWriterSettings { Indent=true }))
                {
                    writer.WriteStartElement("BitrateStatus"); writer.WriteAttributeString("version","1");
                    writer.WriteAttributeString("processId",Process.GetCurrentProcess().Id.ToString());
                    writer.WriteAttributeString("startedUtcTicks",Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString());
                    writer.WriteAttributeString("revision",revision??"invalid"); writer.WriteAttributeString("enabled",current.Enabled?"true":"false");
                    writer.WriteAttributeString("ready",success?"true":"false"); if(error!=null)writer.WriteAttributeString("error",error);
                    if(unsupported)writer.WriteAttributeString("unsupported","true");
                    writer.WriteEndElement();
                }
                if(File.Exists(StatusPath))File.Replace(temp,StatusPath,StatusPath+".bak");else File.Move(temp,StatusPath);
            }
            catch(Exception e) { log("Could not write bitrate status: "+e.Message); }
        }
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool VirtualProtect(IntPtr address,UIntPtr size,uint protect,out uint old);
        [DllImport("kernel32.dll")] private static extern bool FlushInstructionCache(IntPtr process,IntPtr address,UIntPtr size);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] buffer,UIntPtr size,out UIntPtr read);
    }
}
