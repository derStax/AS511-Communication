namespace AS511_Communication;

public class Program {
    private static void Main(string[] args) {
        var c = new S5Connection("COM3");
        c.Connect();
        
        c.Block_Information(0x01, 0x64, out var initialAddress, out var finalAddress);
        
        c.Read(initialAddress, finalAddress);
        
        byte[] data = [0x12, 0x13, 0x14, 0x15];
        c.Write(initialAddress, data);
        
        c.Read(initialAddress, finalAddress);
        
        c.Disconnect();
    }
}
