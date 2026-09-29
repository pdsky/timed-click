using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TimedClick {
public static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseInput { public int dx,dy; public uint mouseData,dwFlags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MouseInput mouse; }
    [StructLayout(LayoutKind.Sequential)] public struct Input { public uint type; public InputUnion data; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll",SetLastError=true)] public static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);
    public static bool Down(int key) { return (GetAsyncKeyState(key)&0x8000)!=0; }
    public static bool Click() {
        Input[] inputs=new Input[2];
        inputs[0].data.mouse.dwFlags=2; inputs[1].data.mouse.dwFlags=4;
        uint sent=SendInput(2,inputs,Marshal.SizeOf(typeof(Input)));
        if(sent==1) { Input[] release={inputs[1]}; SendInput(1,release,Marshal.SizeOf(typeof(Input))); }
        return sent==2;
    }
}
public static class Program {
    public static DateTime WithMilliseconds(DateTime value,int milliseconds) { return value.AddTicks(-(value.Ticks%TimeSpan.TicksPerSecond)).AddMilliseconds(milliseconds); }
    public static DateTime ToUtc(DateTime wall, int offset) { return DateTime.SpecifyKind(wall,DateTimeKind.Unspecified).AddHours(-offset); }
    public static DateTime NextNoon(DateTime utc,int offset) { DateTime now=utc.AddHours(offset); DateTime noon=now.Date.AddHours(12); return noon>now?noon:noon.AddDays(1); }
    public static void SelfTest() {
        DateTime day=new DateTime(2026,9,29,12,0,0);
        if(ToUtc(day,8)!=new DateTime(2026,9,29,4,0,0)) throw new Exception("UTC+8 conversion failed");
        foreach(int ms in new int[]{0,1,150,999}) if(ToUtc(WithMilliseconds(day.AddMilliseconds(537),ms),8)!=new DateTime(2026,9,29,4,0,0).AddMilliseconds(ms)) throw new Exception("Millisecond conversion failed");
        if(NextNoon(new DateTime(2026,9,29,5,0,0),8)!=day.AddDays(1)) throw new Exception("Next noon failed");
        if(NextNoon(new DateTime(2026,9,29,3,0,0),8)!=day) throw new Exception("Today noon failed");
        if(Marshal.SizeOf(typeof(Native.Input))!=(IntPtr.Size==8?40:28)) throw new Exception("Native input layout failed");
        using(MainForm form=new MainForm()) { if(form.Controls.Count<15) throw new Exception("UI controls missing"); }
        Console.WriteLine("PASS: timezone and millisecond conversion (000/001/150/999), next noon, native input layout, UI construction. No mouse input sent.");
    }
}
public class MillisecondInput:NumericUpDown {
    protected override void UpdateEditText() { Text=Value.ToString("000"); }
}
public class MainForm:Form {
    ComboBox zone=new ComboBox(); DateTimePicker when=new DateTimePicker();
    MillisecondInput milliseconds=new MillisecondInput();
    Label clock=new Label(),position=new Label(),status=new Label(),countdown=new Label();
    TextBox url=new TextBox(); Button arm=new Button(),cancel=new Button(),next=new Button(),locate=new Button();
    Stopwatch captureDelay=new Stopwatch();
    System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    Native.Point point; IntPtr target=IntPtr.Zero; bool captured=false,f8=false,f9=false;
    volatile bool running=false; CancellationTokenSource cancellation; DateTime targetUtc;
    int Offset { get { return zone.SelectedIndex==2?0:8; } }
    public MainForm() {
        Native.SetProcessDPIAware(); Text="定时单次点击 v1.3 · 北京 / 新加坡";
        ClientSize=new Size(740,580); Font=new Font("Microsoft YaHei UI",10);
        FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false; StartPosition=FormStartPosition.CenterScreen;
        Label title=LabelAt("定时点击付款",24,18,680,36); title.Font=new Font(Font.FontFamily,20,FontStyle.Bold);
        LabelAt("填写好订单 → 点击“3 秒后记录位置” → 设定时间 → 启动",24,62,680,28);
        url.SetBounds(24,104,550,28); url.Text="https://www.et.cn/pay?order_id=ET20260924155138355108"; Controls.Add(url);
        Button open=ButtonAt("打开订单",586,102,126,32); open.Click+=(s,e)=>{Uri uri;if(Uri.TryCreate(url.Text,UriKind.Absolute,out uri)&&uri.Scheme=="https") Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});else MessageBox.Show("请输入 HTTPS 地址。");};
        LabelAt("时区",24,154,60,26); zone.SetBounds(92,150,270,30); zone.DropDownStyle=ComboBoxStyle.DropDownList;
        zone.Items.AddRange(new object[]{"北京时间 · UTC+08:00","新加坡时间 · UTC+08:00","协调世界时 · UTC+00:00"}); zone.SelectedIndex=0; Controls.Add(zone);
        clock.SetBounds(24,194,680,28); Controls.Add(clock);
        LabelAt("点击时间",24,237,82,28); when.SetBounds(110,233,275,30); when.Format=DateTimePickerFormat.Custom; when.CustomFormat="yyyy-MM-dd HH:mm:ss"; Controls.Add(when);
        LabelAt(".",389,237,14,28); milliseconds.SetBounds(404,233,78,30); milliseconds.Minimum=0; milliseconds.Maximum=999; milliseconds.Text="000"; milliseconds.TextAlign=HorizontalAlignment.Center; Controls.Add(milliseconds); LabelAt("毫秒",488,237,50,28);
        next=ButtonAt("下一个 12:00",550,231,160,34); next.Click+=(s,e)=>{when.Value=Program.NextNoon(DateTime.UtcNow,Offset);milliseconds.Value=0;};
        when.Value=Program.NextNoon(DateTime.UtcNow,Offset);
        position.SetBounds(24,284,690,30); position.Text="尚未定位：点击“3 秒后记录位置”，再把鼠标移到付款按钮。"; Controls.Add(position);
        countdown.SetBounds(24,326,690,40); countdown.Font=new Font(Font.FontFamily,20,FontStyle.Bold); countdown.Text="等待设置"; Controls.Add(countdown);
        arm=ButtonAt("启动定时点击",24,383,210,44); arm.Click+=(s,e)=>Arm();
        cancel=ButtonAt("取消 · F9 / Esc",250,383,210,44); cancel.Enabled=false; cancel.Click+=(s,e)=>Cancel();
        locate=ButtonAt("3 秒后记录位置",480,383,230,44); locate.Click+=(s,e)=>{captured=false;captureDelay.Restart();locate.Enabled=false;arm.Enabled=false;cancel.Enabled=true;status.Text="请在 3 秒内把鼠标移到付款按钮中央，保持不动。不需要点击！";status.ForeColor=Color.DarkGreen;};
        status.SetBounds(24,443,690,48); status.Text="程序启动成功；定时任务尚未启动。支持按钮定位，也可按 F8。"; status.ForeColor=Color.DarkGreen; Controls.Add(status);
        LabelAt("仅点击一次，不刷新、不连点。请勿锁屏或切换标签页。\n使用 Windows 系统时间；请先在系统日期和时间设置中同步时钟。",24,500,690,62);
        zone.SelectedIndexChanged+=(s,e)=>{when.Value=Program.NextNoon(DateTime.UtcNow,Offset);milliseconds.Value=0;};
        timer.Interval=30; timer.Tick+=(s,e)=>Tick(); timer.Start();
        FormClosing+=(s,e)=>{if(cancellation!=null)cancellation.Cancel();timer.Stop();};
    }
    Label LabelAt(string text,int x,int y,int w,int h) { Label l=new Label(){Text=text};l.SetBounds(x,y,w,h);Controls.Add(l);return l; }
    Button ButtonAt(string text,int x,int y,int w,int h) { Button b=new Button(){Text=text};b.SetBounds(x,y,w,h);Controls.Add(b);return b; }
    void Tick() {
        clock.Text="当前所选时区时间："+DateTime.UtcNow.AddHours(Offset).ToString("yyyy-MM-dd HH:mm:ss.fff");
        bool a=Native.Down(0x77),b=Native.Down(0x78);
        if(a&&!f8&&!running) CapturePosition();
        if(captureDelay.IsRunning) {
            countdown.Text="请移动鼠标 · "+Math.Max(0,3-captureDelay.Elapsed.TotalSeconds).ToString("F1")+" 秒后定位";
            if(captureDelay.ElapsedMilliseconds>=3000) CapturePosition();
        }
        if((b&&!f9)|| ((running||captureDelay.IsRunning)&&Native.Down(0x1B))) Cancel(); f8=a;f9=b;
        if(running) { TimeSpan d=targetUtc-DateTime.UtcNow; countdown.Text=d.TotalSeconds>0?"剩余 "+((int)d.TotalDays)+"天 "+d.ToString(@"hh\:mm\:ss\.fff"):"正在触发…"; }
    }
    void CapturePosition() {
        captureDelay.Reset(); locate.Enabled=true;arm.Enabled=true;cancel.Enabled=false;
        bool read=Native.GetCursorPos(out point); target=read?Native.GetAncestor(Native.WindowFromPoint(point),2):IntPtr.Zero;
        captured=target!=IntPtr.Zero&&target!=Handle;
        position.Text=captured?"已记录屏幕坐标：X="+point.X+"，Y="+point.Y+"。移动窗口后请重新定位。":"定位失败：鼠标仍在工具上或无法读取位置，请重新定位。";
        status.Text=captured?"定位成功！请设置未来时间，再点击“启动定时点击”。":"请点击“3 秒后记录位置”，然后移到浏览器付款按钮中央。";
        status.ForeColor=captured?Color.DarkGreen:Color.Firebrick;
        countdown.Text=captured?"定位成功 · 等待启动":"定位失败 · 请重试";
        if(captured) System.Media.SystemSounds.Asterisk.Play();
    }
    void Cancel() {
        if(cancellation!=null)cancellation.Cancel();
        if(captureDelay.IsRunning) {captureDelay.Reset();locate.Enabled=true;arm.Enabled=true;cancel.Enabled=false;countdown.Text="已取消定位";status.Text="定位已取消，尚未启动定时任务。";}
    }
    void ShowStarted() {
        Form notice=new Form(); notice.Text="定时任务启动成功"; notice.ClientSize=new Size(490,140);
        notice.Font=Font; notice.FormBorderStyle=FormBorderStyle.FixedToolWindow; notice.StartPosition=FormStartPosition.CenterParent; notice.ShowInTaskbar=false;
        Label message=new Label(); message.SetBounds(16,16,460,112); message.ForeColor=Color.DarkGreen;
        message.Text="定时任务启动成功！\n目标："+targetUtc.AddHours(Offset).ToString("yyyy-MM-dd HH:mm:ss.fff")+"（UTC+"+Offset.ToString("00")+"）\n请立即切回付款页。到点点击一次，F9 可取消。\n本提示 2 秒后自动关闭，倒计时继续运行。";
        notice.Controls.Add(message);
        System.Windows.Forms.Timer hide=new System.Windows.Forms.Timer(); hide.Interval=2000;
        hide.Tick+=(s,e)=>notice.Close(); notice.FormClosed+=(s,e)=>hide.Dispose();
        notice.Show(this); hide.Start();
        System.Media.SystemSounds.Asterisk.Play();
    }
    void Arm() {
        if(!captured||!Native.IsWindow(target)) {MessageBox.Show("请先点击“3 秒后记录位置”，并把鼠标移到付款按钮。也可按 F8 定位。");return;}
        DateTime selected=Program.WithMilliseconds(when.Value,(int)milliseconds.Value);
        targetUtc=Program.ToUtc(selected,Offset); double seconds=(targetUtc-DateTime.UtcNow).TotalSeconds;
        if(seconds<3||seconds>86400*7) {MessageBox.Show("请选择至少 3 秒后、7 天内的时间。");return;}
        cancellation=new CancellationTokenSource(); CancellationToken token=cancellation.Token;
        running=true; arm.Enabled=false; zone.Enabled=false; when.Enabled=false; milliseconds.Enabled=false; next.Enabled=false;locate.Enabled=false;cancel.Enabled=true;
        Thread worker=new Thread(()=>Run(token,point,target,targetUtc)){IsBackground=true};worker.Start();
        status.ForeColor=Color.DarkGreen; status.BackColor=Color.Honeydew;
        status.Text="定时任务启动成功！目标："+selected.ToString("yyyy-MM-dd HH:mm:ss.fff")+"\n请切回付款页并保持前台；F9 或 Esc 取消。";
        arm.Text="已启动 · 等待点击";
        ShowStarted();
    }
    void Run(CancellationToken token,Native.Point p,IntPtr hwnd,DateTime due) {
        string result="已取消。"; bool resolution=Native.timeBeginPeriod(1)==0;
        Native.SetThreadExecutionState(0x80000003);
        DateTime start=DateTime.UtcNow; Stopwatch elapsed=Stopwatch.StartNew();
        try {
            while(true) {
                if(token.IsCancellationRequested||Native.Down(0x78)||Native.Down(0x1B)) break;
                DateTime now=DateTime.UtcNow;
                if(Math.Abs((now-start).TotalMilliseconds-elapsed.Elapsed.TotalMilliseconds)>1000) {result="系统时间发生跳变，已取消。同步时钟后请重新启动。";break;}
                double remaining=(due-now).TotalMilliseconds;
                if(remaining<=0) {
                    if(remaining < -250) {result="电脑调度延迟超过 250 毫秒，已取消，避免过期点击。";break;}
                    if(!Native.IsWindow(hwnd)||Native.GetForegroundWindow()!=hwnd||Native.GetAncestor(Native.WindowFromPoint(p),2)!=hwnd) {result="付款窗口不在前台或按钮位置被遮挡，已取消点击。";break;}
                    if(token.IsCancellationRequested||Native.Down(0x78)||Native.Down(0x1B)) break;
                    if(!Native.SetCursorPos(p.X,p.Y)) {result="无法移动鼠标，未点击。";break;}
                    double late=(DateTime.UtcNow-due).TotalMilliseconds;
                    if(late>250) {result="点击前延迟超过 250 毫秒，已取消。";break;}
                    result=Native.Click()?"已发送一次点击。触发延迟约 "+late.ToString("F1")+" 毫秒；请在网页查看付款结果。":"系统未完整接受点击，请在网页检查结果。";
                    break;
                }
                Thread.Sleep(remaining>1000?25:remaining>50?5:1);
            }
        } catch(Exception ex) {result="已停止："+ex.Message;}
        finally {if(resolution)Native.timeEndPeriod(1);Native.SetThreadExecutionState(0x80000000);}
        if(!IsDisposed&&IsHandleCreated) try {BeginInvoke((Action)(()=>{running=false;arm.Enabled=true;arm.Text="启动定时点击";zone.Enabled=true;when.Enabled=true;milliseconds.Enabled=true;next.Enabled=true;locate.Enabled=true;cancel.Enabled=false;status.BackColor=SystemColors.Control;status.ForeColor=SystemColors.ControlText;status.Text=result;countdown.Text="已停止";}));} catch(InvalidOperationException) {}
    }
}
}
