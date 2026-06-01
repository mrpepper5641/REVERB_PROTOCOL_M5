using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using UnityEngine;

public class M5Reader : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────
    public static M5Reader Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    [Header("Serial Settings")]
    [SerializeField] private int baudRate = 115200;
    [SerializeField] private string handshakeString = "M5_HANDSHAKE_v1";

    [Header("Live Values (read-only)")]
    [SerializeField] private bool isConnected = false;
    [SerializeField] private string connectedPort = "";
    [SerializeField] private float pitch;
    [SerializeField] private float roll;
    [SerializeField] private float ax, ay, az;
    [SerializeField] private float tx, ty;
    [SerializeField] private int btnA, btnB, btnC;

    // public accessors for other scripts
    [Header("Hack Minigame (read-only)")]
    [SerializeField] private int hackState;    // 0=idle 1=touch1 2=touch2 3=tilt1 4=tilt2 5=success 6=fail
    [SerializeField] private int hackProgress; // 0-100

    public bool IsConnected  => isConnected;
    public float Pitch       => pitch;
    public float Roll        => roll;
    public Vector3 Accel     => new Vector3(ax, ay, az);
    public Vector2 Touch     => new Vector2(tx, ty);
    public bool ButtonA      => btnA == 1;
    public bool ButtonB      => btnB == 1;
    public bool ButtonC      => btnC == 1;
    public int HackState     => hackState;
    public int HackProgress  => hackProgress;

    private SerialPort serialPort;
    private Thread readThread;
    private volatile bool keepReading = false;
    private readonly ConcurrentQueue<string> incomingLines = new ConcurrentQueue<string>();

    private float portScanTimer = 0f;
    private const float PORT_SCAN_INTERVAL = 1.0f;

    void Update()
    {
        if (!isConnected)
        {
            portScanTimer += Time.deltaTime;
            if (portScanTimer >= PORT_SCAN_INTERVAL)
            {
                portScanTimer = 0f;
                TryConnect();
            }
            return;
        }

        // drain queued lines from the read thread
        while (incomingLines.TryDequeue(out string line))
        {
            ParseLine(line);
        }
    }

    private void TryConnect()
    {
        string[] ports = SerialPort.GetPortNames();
        foreach (string portName in ports.Reverse()) // newer ports tend to be last
        {
            if (TryOpenAndHandshake(portName))
            {
                isConnected = true;
                connectedPort = portName;
                Debug.Log($"[M5Reader] Connected on {portName}");
                StartReadThread();
                return;
            }
        }
    }

    private bool TryOpenAndHandshake(string portName)
    {
        SerialPort sp = null;
        try
        {
            sp = new SerialPort(portName, baudRate);
            sp.ReadTimeout = 500;
            sp.NewLine = "\n";
            sp.Open();

            // wait for handshake (up to ~1.5 sec)
            DateTime deadline = DateTime.Now.AddMilliseconds(1500);
            while (DateTime.Now < deadline)
            {
                try
                {
                    string line = sp.ReadLine().Trim();
                    if (line.Contains(handshakeString))
                    {
                        // handshake matched, keep this port open
                        serialPort = sp;
                        return true;
                    }
                }
                catch (TimeoutException) { /* keep trying */ }
            }

            sp.Close();
            return false;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[M5Reader] Failed to open {portName}: {e.Message}");
            try { sp?.Close(); } catch { }
            return false;
        }
    }

    private void StartReadThread()
    {
        keepReading = true;
        readThread = new Thread(ReadLoop) { IsBackground = true };
        readThread.Start();
    }

    private void ReadLoop()
    {
        while (keepReading && serialPort != null && serialPort.IsOpen)
        {
            try
            {
                string line = serialPort.ReadLine();
                if (!string.IsNullOrEmpty(line))
                {
                    incomingLines.Enqueue(line.Trim());
                }
            }
            catch (TimeoutException) { /* normal */ }
            catch (Exception e)
            {
                Debug.LogWarning($"[M5Reader] Read error: {e.Message}");
                break;
            }
        }
    }

    private void ParseLine(string line)
    {
        // expected: D,P,R,AX,AY,AZ,TX,TY,A,B,C
        if (!line.StartsWith("D,")) return;

        string[] parts = line.Split(',');
        if (parts.Length < 11) return;

        try
        {
            pitch = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            roll  = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            ax    = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
            ay    = float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture);
            az    = float.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture);
            tx    = float.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture);
            ty    = float.Parse(parts[7], System.Globalization.CultureInfo.InvariantCulture);
            btnA  = int.Parse(parts[8]);
            btnB  = int.Parse(parts[9]);
            btnC  = int.Parse(parts[10]);

            // hack fields (optional: present only when M5 firmware supports them)
            if (parts.Length >= 12)
                hackState = int.Parse(parts[11]);
            if (parts.Length >= 13)
                hackProgress = int.Parse(parts[12]);
        }
        catch
        {
            // ignore parse errors (rare malformed lines)
        }
    }

    // Send a command line to the M5 (called from main thread or any thread)
    public void SendCommand(string cmd)
    {
        if (!isConnected || serialPort == null || !serialPort.IsOpen) return;
        try
        {
            serialPort.WriteLine(cmd);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[M5Reader] SendCommand failed: {e.Message}");
        }
    }

    void OnApplicationQuit() => Disconnect();
    void OnDestroy() => Disconnect();

    private void Disconnect()
    {
        keepReading = false;
        try { readThread?.Join(500); } catch { }
        try { serialPort?.Close(); } catch { }
        serialPort = null;
        isConnected = false;
    }
}