using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AtkBatteryTray {
    /// <summary>
    /// 通过 Windows.Media.Audio.SpatialAudioDeviceConfiguration 直接读写指定回放端点的
    /// 空间音效格式。开启 Windows Sonic 即获得对所有耳机通用的 7.1 虚拟环绕，
    /// 无需管理员权限，也不需要厂商驱动。
    /// </summary>
    internal static class SpatialAudio {
        internal sealed class Result {
            internal bool Supported,Enabled;
            internal string Format="",Error="";
            internal bool Ok {get{return String.IsNullOrEmpty(Error);}}
        }

        const string RendererSuffix="#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
        const string SonicFormat="{B53D940C-B846-4831-9F76-D102B9B725A0}";
        const string DisabledFormat="{00000000-0000-0000-0000-000000000000}";

        static string ScriptPath {
            get {
                string dir=Path.Combine(Path.GetTempPath(),"MyFinderDeviceBattery");
                try { Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir,"spatial-audio.ps1");
            }
        }

        static readonly string Script=@"
param([string]$DeviceId,[string]$Action)
$ErrorActionPreference='Stop'
try {
  Add-Type -AssemblyName System.Runtime.WindowsRuntime
  [Windows.Foundation.IAsyncOperation`1,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
  [Windows.Devices.Enumeration.DeviceInformation,Windows.Devices.Enumeration,ContentType=WindowsRuntime] | Out-Null
  [Windows.Devices.Enumeration.DeviceInformationCollection,Windows.Devices.Enumeration,ContentType=WindowsRuntime] | Out-Null
  [Windows.Media.Audio.SpatialAudioDeviceConfiguration,Windows.Media.Audio,ContentType=WindowsRuntime] | Out-Null
  [Windows.Media.Audio.SpatialAudioFormatSubtype,Windows.Media.Audio,ContentType=WindowsRuntime] | Out-Null
  [Windows.Media.Audio.SetDefaultSpatialAudioFormatResult,Windows.Media.Audio,ContentType=WindowsRuntime] | Out-Null
  $asTaskGeneric=([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
  function Await($op,$type) { $m=$asTaskGeneric.MakeGenericMethod($type); $t=$m.Invoke($null,@($op)); $t.Wait(8000) | Out-Null; return $t.Result }
  $full='\\?\SWD#MMDEVAPI#'+$DeviceId+'#{e6327cad-dcec-4949-ae8a-991e976a79d2}'
  $cfg=[Windows.Media.Audio.SpatialAudioDeviceConfiguration]::GetForDeviceId($full)
  $sonic=[Windows.Media.Audio.SpatialAudioFormatSubtype]::WindowsSonic
  if($Action -eq 'on' -or $Action -eq 'off') {
    if(-not $cfg.IsSpatialAudioSupported) { Write-Output 'ERR|unsupported'; exit 0 }
    if($Action -eq 'on' -and -not $cfg.IsSpatialAudioFormatSupported($sonic)) { Write-Output 'ERR|format-unsupported'; exit 0 }
    $target=if($Action -eq 'on') { $sonic } else { '{00000000-0000-0000-0000-000000000000}' }
    $r=Await ($cfg.SetDefaultSpatialAudioFormatAsync($target)) ([Windows.Media.Audio.SetDefaultSpatialAudioFormatResult])
    if([string]$r.Status -ne 'Succeeded') { Write-Output ('ERR|status-' + [string]$r.Status); exit 0 }
    Start-Sleep -Milliseconds 350
    $cfg=[Windows.Media.Audio.SpatialAudioDeviceConfiguration]::GetForDeviceId($full)
  }
  Write-Output ('OK|' + [string]$cfg.ActiveSpatialAudioFormat + '|' + [string]$cfg.IsSpatialAudioSupported)
} catch {
  $msg=[string]$_.Exception.Message
  $msg=$msg.Replace([char]13,' ').Replace([char]10,' ')
  Write-Output ('ERR|exception-' + $msg)
}
";

        internal static Result Query(string deviceId) {
            return Run(deviceId,"query");
        }

        internal static Result Set(string deviceId,bool enable) {
            return Run(deviceId,enable?"on":"off");
        }

        static Result Run(string deviceId,string action) {
            var result=new Result();
            if(String.IsNullOrEmpty(deviceId)){result.Error="未找到耳机音频端点";return result;}
            // 调用方传入的是 MMDevice 端点 ID：{0.0.0.00000000}.{GUID}
            if(deviceId.IndexOf("MMDEVAPI#",StringComparison.OrdinalIgnoreCase)>=0) {
                int start=deviceId.IndexOf("MMDEVAPI#",StringComparison.OrdinalIgnoreCase)+9;
                int end=deviceId.IndexOf('#',start);
                deviceId=end>start?deviceId.Substring(start,end-start):deviceId;
            }
            EnsureScript();
            var info=new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+ScriptPath+"\" -DeviceId \""+deviceId.Replace("\"","")+"\" -Action "+action) {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true
            };
            try {
                using(var process=Process.Start(info)) {
                    string stdout=process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    if(!process.WaitForExit(12000)) {
                        try { process.Kill(); } catch { }
                        result.Error="空间音效服务响应超时";
                        return result;
                    }
                    string line=null;
                    foreach(string candidate in stdout.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
                        string value=candidate.Trim();
                        if(value.StartsWith("OK|",StringComparison.Ordinal)||value.StartsWith("ERR|",StringComparison.Ordinal)){line=value;break;}
                    }
                    if(line==null){result.Error="空间音效接口未返回结果";return result;}
                    if(line.StartsWith("ERR|",StringComparison.Ordinal)) {
                        string code=line.Substring(4);
                        result.Error=code=="unsupported"?"该播放设备不支持空间音效"
                            :code=="format-unsupported"?"该播放设备不支持 Windows Sonic"
                            :code.StartsWith("status-")?"空间音效切换失败："+code.Substring(7)
                            :code.StartsWith("exception-")?"空间音效接口不可用："+code.Substring(10)
                            :"空间音效切换失败："+code;
                        return result;
                    }
                    var parts=line.Substring(3).Split('|');
                    result.Format=parts.Length>0?parts[0]:"";
                    bool supported;result.Supported=parts.Length>1&&Boolean.TryParse(parts[1],out supported)?supported:true;
                    result.Enabled=!result.Format.Equals(DisabledFormat,StringComparison.OrdinalIgnoreCase)&&!String.IsNullOrEmpty(result.Format);
                    return result;
                }
            } catch(Exception e) {
                result.Error="空间音效接口不可用："+e.Message;
                return result;
            }
        }

        static void EnsureScript() {
            try {
                string path=ScriptPath;
                bool write=true;
                if(File.Exists(path)) {
                    try { write=File.ReadAllText(path,Encoding.UTF8)!=Script; } catch { write=true; }
                }
                if(write) File.WriteAllText(path,Script,new UTF8Encoding(true));
            } catch { }
        }

        internal static bool IsEnabled(string deviceId) {
            var result=Query(deviceId);
            return result.Ok&&result.Enabled;
        }
    }
}
