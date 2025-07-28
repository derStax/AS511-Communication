namespace AS511_Communication;

public class Program {
    private static void Main(string[] args) {
        /*
        This project is an unofficial, open-source implementation for communicating with Siemens S5 PLCs using the AS511 protocol.  
        It is **not affiliated with or endorsed by Siemens AG**.  
        "Siemens", "S5", "SPS", and related terms may be trademarks or registered trademarks of Siemens AG or other respective owners.  
        They are used in this project strictly for technical reference and descriptive purposes.
         */
        var c = new S5Connection(GetPort());
        c.Connect();
        
        c.Block_Information(0x01, 0x64, out var initialAddress, out var finalAddress);
        
        var readData = c.Read(initialAddress, finalAddress);
        Console.WriteLine(readData.ToHexString());
        
        byte[] data = [0x12, 0x13, 0x14, 0x15];
        c.Write(initialAddress, data);
        
        c.Read(initialAddress, finalAddress);
        
        c.Disconnect();
    }
    
    private static string GetPort() {
        if (OperatingSystem.IsWindows())
            return "COM3";
        else if (OperatingSystem.IsLinux())
            return "/dev/ttyUSB0";
        else
            return "UNKNOWN";
    }
    
    public static string ToHexString(this byte[] bytes, string divisor = " ")
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        return BitConverter.ToString(bytes).Replace("-", divisor);
    }
}
