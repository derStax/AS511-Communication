namespace AS511_Communication;

using System;
using System.IO.Ports;
using System.Text;

/// <summary>
/// Create a connection to a Siemens SPS S5 via AS511 Protocol.
/// <para/>The AS511 communication gets imitated, reading and writing the DLE, ACK (...and more) commands manually.
/// <para/>Please keep it mind, that this script only provides an imitated workaround, and to not rely on this for important machines.
/// <para/>The Protocol-Structured got analyzed here: https://www.runmode.com/as511protocol_description.pdf
/// </summary>
public class S5Connection {
    private readonly SerialPort _port;

    // the following Control-Character descriptions can be found on Wikipedia
    // https://de.wikipedia.org/wiki/Steuerzeichen 
    // https://en.wikipedia.org/wiki/Control_character 
    /// <summary>Start of Text</summary>
    private const byte STX = 0x02;
    /// <summary>End of Text</summary>
    private const byte ETX = 0x03;
    /// <summary>End of Transmission</summary>
    private const byte EOT = 0x04;
    /// <summary>Acknowledge</summary>
    private const byte ACK = 0x06;
    /// <summary>Data Link Escape</summary>
    private const byte DLE = 0x10;
    /// <summary>AG "end of transmission"</summary>
    private const byte AG_END = 0x12;
    
    private const byte DB_READ_CODE = 0x04;
    private const byte DB_WRITE_CODE = 0x03;
    private const byte BLOCK_INFO = 0x1A;
    
    
    /// <summary>
    /// Create the SerialPort connection. Needs to be connected afterward.
    /// </summary>
    /// <param name="portName">Port of the OS (in Windows mostly 'COM3')</param>
    public S5Connection(string portName) {
        _port = new SerialPort(portName, 9600, Parity.Even, 8, StopBits.One);
        _port.Encoding = Encoding.ASCII;
        _port.ReadTimeout = 1000;
        _port.WriteTimeout = 1000;
    }
    #region Helper
    public void Connect() => _port.Open();
    public void Disconnect() => _port.Close();
    /// <summary>
    /// Writes a single Byte to the S5.
    /// </summary>
    /// <param name="b">Byte to Write</param>
    /// <param name="callerName">Name of the Method, which called this (debug purposes)</param>
    /// <param name="offset">(Optional) Offset of the SerialPort.Write command</param>
    /// <param name="count">(Optional) Count of the SerialPort.Write command</param>
    private void WriteSingle(byte b, string callerName, int offset = 0, int count = 1) {
        try {
            _port.Write([b], offset, count);
        }
        catch (Exception e) {
            Console.WriteLine("Caller: " + callerName);
            Console.WriteLine(e);
        }
    }
    /// <summary>
    /// Reads a single byte from the S5, and compares it to an expected byte
    /// </summary>
    /// <param name="expected">Expected Byte - if this doesn't match with the Byte that got read, there will be a message in the cconsole.</param>
    /// <param name="callerName">Name of the Method, which called this (debug purposes)</param>
    private void ReadSingle(byte expected, string callerName) {
        try {
            var read = _port.ReadByte();
            if (read == expected)
                return;
            Console.WriteLine("Caller: " + callerName);
            Console.WriteLine("Expected: " + expected + ", Received: " + read);
        }
        catch (Exception e) {
            Console.WriteLine("Caller: " + callerName);
            Console.WriteLine(e);
        }
    }
    #endregion
    
    /// <summary>
    /// Request Block information. If required, there is also a variable of the blockLength here. 
    /// </summary>
    /// <param name="id">ID, of which kind of Block is meant (DB, SB, PB...)</param>
    /// <param name="nr">Block Nr (e.g. DB100 -> nr=100)</param>
    /// <param name="initialAddress">initial absolute address in PLC memory</param>
    /// <param name="finalAddress">final absolute address in PLC memory (gets calculated by initialAddress + blockLength)</param>
    public void Block_Information(byte id, byte nr, out byte[] initialAddress, out byte[] finalAddress) {
        const string caller = nameof(Block_Information);

        WriteSingle(STX, caller);
        ReadSingle(DLE, caller);
        ReadSingle(ACK, caller);
        WriteSingle(BLOCK_INFO, caller);
        ReadSingle(STX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
        ReadSingle(0x16, caller);
        ReadSingle(DLE, caller);
        ReadSingle(ETX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
        
        // === Header Info ===
        WriteSingle(id, caller); // type (Datablock, Structured Block...)
        WriteSingle(nr, caller); // nr (0..255)
        WriteSingle(DLE, caller);
        WriteSingle(EOT, caller);
        ReadSingle(DLE, caller);
        ReadSingle(ACK, caller);
        // === Data ===
        ReadSingle(STX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
        
        List<byte> buffer = new List<byte>();
        while (true)
        {
            int b = _port.ReadByte();
            buffer.Add((byte)b);
            if (b == 0x03)  // ETX empfangen
                break;
        }
        
        // read initialAddress, calculate final address (initialAddress + blockLength)
        initialAddress = [buffer[1], buffer[2]];
        byte[] blockLength = [buffer[11], buffer[12]];
        var initial = (ushort)((initialAddress[0] << 8) | initialAddress[1]);
        var length  = (ushort)((blockLength[0] << 8) | blockLength[1]);
        var final = (ushort)(initial + length);
        finalAddress = [(byte)(final >> 8), (byte)(final & 0xFF)];

        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
        
        // === terminate === 
        ReadSingle(STX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
        ReadSingle(AG_END, caller);
        ReadSingle(DLE, caller);
        ReadSingle(ETX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
    }

    /// <summary>
    /// Read information from PLC memory
    /// </summary>
    /// <param name="initialAddress">initial absolute address (read start address)</param>
    /// <param name="finalAddress">final absolute address (read stop address)</param>
    /// <returns>byte array, of all the bytes that got read.</returns>
    public byte[] Read(byte[] initialAddress, byte[] finalAddress) {
        const string caller = nameof(Read); // debug purposes
        
        WriteSingle(STX, caller);
        ReadSingle(DLE, caller);
        ReadSingle(ACK, caller);
        WriteSingle(DB_READ_CODE, caller);
        ReadSingle(STX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
        ReadSingle(0x16, caller);
        ReadSingle(DLE, caller);
        ReadSingle(ETX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);

        // === header=== 
        _port.Write(initialAddress, 0, 2);
        _port.Write(finalAddress, 0, 2);
        WriteSingle(DLE, caller);
        WriteSingle(EOT, caller);
        ReadSingle(DLE, caller);
        ReadSingle(ACK, caller);
        // === data ===
        ReadSingle(STX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
        
        //Console.WriteLine("\nREADING:");
        List<byte> buffer = new List<byte>();
        while (true)
        {
            int b = _port.ReadByte();
            buffer.Add((byte)b);
            if (b == 0x03)  // ETX empfangen - stop reading, start terminating handshake
                break;
        }
        
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
        
        // === terminate === 
        ReadSingle(STX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
        ReadSingle(AG_END, caller);
        ReadSingle(DLE, caller);
        ReadSingle(ETX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);

        return buffer.ToArray();
    }

    /// <summary>
    /// Write information to PLC memory
    /// </summary>
    /// <param name="initialAddress">initial absolute address (write start address)</param>
    /// <param name="data">byte data, which will get written to the memory, beginning at initial address</param>
    public void Write(byte[] initialAddress, byte[] data) {
        const string caller = nameof(Write);
        
        WriteSingle(STX, caller);
        ReadSingle(DLE, caller);
        ReadSingle(ACK, caller);
        WriteSingle(DB_WRITE_CODE, caller);
        ReadSingle(STX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
        ReadSingle(0x16, caller);
        ReadSingle(DLE, caller);
        ReadSingle(ETX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
        // === header info ===
        _port.Write(initialAddress, 0, 2);
        _port.Write(data, 0, data.Length);
        WriteSingle(DLE, caller);
        WriteSingle(EOT, caller);
        ReadSingle(DLE, caller);
        ReadSingle(ACK, caller);
        // === terminate ===
        ReadSingle(STX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
        ReadSingle(AG_END, caller);
        ReadSingle(DLE, caller);
        ReadSingle(ETX, caller);
        WriteSingle(DLE, caller);
        WriteSingle(ACK, caller);
    }
}