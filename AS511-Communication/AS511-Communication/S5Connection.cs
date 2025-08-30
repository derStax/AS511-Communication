using System.IO.Ports;
using System.Text;

namespace AS511_Communication;

/// <summary>
/// Create a connection to a Siemens SPS S5 via AS511 Protocol.
/// <para/>The AS511 communication gets imitated, reading and writing the DLE, ACK (...and more) commands manually.
/// <para/>Please keep it mind, that this script only provides an imitated workaround, and to not rely on this for important machines.
/// <para/>The Protocol-Structured got analyzed here: https://www.runmode.com/as511protocol_description.pdf 
/// </summary>
public class S5Connection {
    private readonly SerialPort _port;

    #region Byte Variables

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

    #endregion

    /// <summary> Create the SerialPort connection. Needs to be connected afterward. </summary>
    /// <param name="portName">Port of the OS (in Windows mostly 'COM3')</param>
    public S5Connection(string portName) {
        _port = new SerialPort(portName, 9600, Parity.Even, 8, StopBits.One);
        _port.Encoding = Encoding.ASCII;
        _port.ReadTimeout = 1000;
        _port.WriteTimeout = 1000;
        Connect();
    }

    /// <summary> Request Block information. If required, there is also a variable of the blockLength here. </summary>
    /// <param name="id">ID, of which kind of Block is meant (DB, SB, PB...)</param>
    /// <param name="nr">Block Nr (e.g. DB100 -> nr=100)</param>
    /// <param name="initialAddress">initial absolute address in PLC memory</param>
    /// <param name="blockLength">length of the block (how many words, each word = 2bytes)</param>
    /// <param name="finalAddress">final absolute address in PLC memory (gets calculated by initialAddress + blockLength)</param>
    public void BlockInformation(byte id, byte nr, out byte[] initialAddress, out ushort blockLength,
        out byte[] finalAddress) {
        WriteSingle(STX);
        ReadSingle(DLE);
        ReadSingle(ACK);
        WriteSingle(BLOCK_INFO);
        ReadSingle(STX);
        WriteSingle(DLE);
        WriteSingle(ACK);
        ReadSingle(0x16);
        ReadSingle(DLE);
        ReadSingle(ETX);
        WriteSingle(DLE);
        WriteSingle(ACK);

        // === Header Info ===
        WriteSingle(id); // type (Datablock, Structured Block...)
        WriteSingle(nr); // nr (0..255)
        WriteSingle(DLE);
        WriteSingle(EOT);
        ReadSingle(DLE);
        ReadSingle(ACK);
        // === Data ===
        ReadSingle(STX);
        WriteSingle(DLE);
        WriteSingle(ACK);

        List<byte> buffer = [];
        // saves, if the previous read byte was a "double" DLE (data)byte. If so, and now another DLE comes, it must 
        // be a "real" DLE, and therefore CANT be skipped!
        var previousDoubleDle = false;
        try {
            while (true) {
                int b = ReadSingle();
                // if second DLE, skip this byte
                if (b == 0x10) {
                    if (buffer.Count > 1) {
                        if (buffer[^1] == 0x10) {
                            if (!previousDoubleDle) {
                                previousDoubleDle = true;
                                continue;
                            }
                        }
                    }
                }

                previousDoubleDle = false;
                buffer.Add((byte)b);

                // 15 "real" databytes (including 0x00 in front and DLE ETX at end, excluding double DLE)
                if (buffer.Count == 15)
                    if (b == 0x03) // current ETX
                        if (buffer[^2] == DLE) // previous DLE
                            break;
            }
        } catch (Exception e) {
            Console.WriteLine("Exception: " + e);
        }

        // read initialAddress, calculate final address (initialAddress + blockLength)
        initialAddress = [buffer[1], buffer[2]];
        byte[] blockLengthArr = [buffer[11], buffer[12]];
        var initial = (ushort)((initialAddress[0] << 8) | initialAddress[1]);
        blockLength = (ushort)((blockLengthArr[0] << 8) | blockLengthArr[1]);
        var final = (ushort)(initial + blockLength);
        finalAddress = [(byte)(final >> 8), (byte)(final & 0xFF)];

        WriteSingle(DLE);
        WriteSingle(ACK);

        // === terminate === 
        ReadSingle(STX);
        WriteSingle(DLE);
        WriteSingle(ACK);
        ReadSingle(AG_END);
        ReadSingle(DLE);
        ReadSingle(ETX);
        WriteSingle(DLE);
        WriteSingle(ACK);
    }

    /// <summary> Read information from PLC memory </summary>
    /// <param name="initialAddress">initial absolute address (read start address)</param>
    /// <param name="blockLength">blockLength, checks if all memory already got read</param>
    /// <param name="finalAddress">final absolute address (read stop address)</param>
    /// <returns>byte array, of all the bytes that got read.</returns>
    public byte[] Read(byte[] initialAddress, ushort blockLength, byte[] finalAddress) {
        WriteSingle(STX);
        ReadSingle(DLE);
        ReadSingle(ACK);
        WriteSingle(DB_READ_CODE);
        ReadSingle(STX);
        WriteSingle(DLE);
        WriteSingle(ACK);
        ReadSingle(0x16);
        ReadSingle(DLE);
        ReadSingle(ETX);
        WriteSingle(DLE);
        WriteSingle(ACK);

        // === header=== 
        _port.Write(initialAddress, 0, 2);
        _port.Write(finalAddress, 0, 2);
        WriteSingle(DLE);
        WriteSingle(EOT);
        ReadSingle(DLE);
        ReadSingle(ACK);
        // === data ===
        ReadSingle(STX);
        WriteSingle(DLE);
        WriteSingle(ACK);

        List<byte> buffer = new List<byte>();
        try {
            ushort curLength = 0;
            var previousDoubleDle = false;
            while (true) {
                int b = ReadSingle();
                if (b == 0x10) {
                    if (buffer.Count > 1) {
                        if (buffer[^1] == 0x10) {
                            if (!previousDoubleDle) {
                                previousDoubleDle = true;
                                continue;
                            }
                        }
                    }
                }

                previousDoubleDle = false;

                curLength++;
                buffer.Add((byte)b);

                // we will read 5 times 0x00 before reading the actual data
                // at the end, we will read 0x10, 0x03
                // therefore, we will read 7 Bytes more, then the actual data is long (blockLength)
                // somehow, we need to increase block length by 8 instead of 7. I dont know why, but it seems to work :)
                if (curLength == blockLength + 8) {
                    // ETX - stop reading, start terminating handshake
                    if (b == 0x03) {
                        break;
                    }
                }
            }
        } catch (Exception e) {
            Console.WriteLine("Exception: " + e);
        }

        WriteSingle(DLE);
        WriteSingle(ACK);

        // === terminate === 
        ReadSingle(STX);
        WriteSingle(DLE);
        WriteSingle(ACK);
        ReadSingle(AG_END);
        ReadSingle(DLE);
        ReadSingle(ETX);
        WriteSingle(DLE);
        WriteSingle(ACK);

        return buffer.ToArray();
    }

    /// <summary>
    /// Write information to PLC memory
    /// </summary>
    /// <param name="initialAddress">initial absolute address (write start address)</param>
    /// <param name="data">byte data, which will get written to the memory, beginning at initial address</param>
    public void Write(byte[] initialAddress, byte[] data) {
        WriteSingle(STX);
        ReadSingle(DLE);
        ReadSingle(ACK);
        WriteSingle(DB_WRITE_CODE);
        ReadSingle(STX);
        WriteSingle(DLE);
        WriteSingle(ACK);
        ReadSingle(0x16);
        ReadSingle(DLE);
        ReadSingle(ETX);
        WriteSingle(DLE);
        WriteSingle(ACK);
        // === header info ===
        _port.Write(initialAddress, 0, 2);

        // write double 0x10 to verify, that we want to use 0x10 instead of DLE
        foreach (var b in data) {
            _port.Write([b], 0, 1);
            if (b == DLE) {
                _port.Write([b], 0, 1);
            }
        }

        //_port.Write(data, 0, data.Length);

        WriteSingle(DLE);
        WriteSingle(EOT);
        ReadSingle(DLE);
        ReadSingle(ACK);
        // === terminate ===
        ReadSingle(STX);
        WriteSingle(DLE);
        WriteSingle(ACK);
        ReadSingle(AG_END);
        ReadSingle(DLE);
        ReadSingle(ETX);
        WriteSingle(DLE);
        WriteSingle(ACK);
    }

    public void Flush() {
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
    }

    #region Helper

    private void Connect() => _port.Open();
    public void Disconnect() => _port.Close();

    /// <summary> Writes a single Byte to the S5. </summary>
    /// <param name="b">Byte to Write</param>
    /// <param name="offset">(Optional) Offset of the SerialPort.Write command</param>
    /// <param name="count">(Optional) Count of the SerialPort.Write command</param>
    private void WriteSingle(byte b, int offset = 0, int count = 1) {
        try {
            _port.Write([b], offset, count);
        } catch (Exception e) {
            Console.WriteLine("Exception: " + e);
        }
    }

    /// <summary> Reads a single byte from the S5, and compares it to an expected byte </summary>
    /// <param name="expected">Expected Byte - if this doesn't match with the Byte that got read, there will be a message in the console.</param>
    private byte ReadSingle(byte expected) {
        try {
            var read = _port.ReadByte();
            if (read == expected)
                return (byte)read;
            Console.WriteLine("Expected: " + expected + ", Received: " + read);
        } catch (Exception e) {
            Console.WriteLine("Exception: " + e);
        }

        return 0;
    }

    private byte ReadSingle() {
        try {
            var read = _port.ReadByte();
            return (byte)read;
        } catch (Exception e) {
            Console.WriteLine("Exception: " + e);
        }
        return 0;
    }

    #endregion
}