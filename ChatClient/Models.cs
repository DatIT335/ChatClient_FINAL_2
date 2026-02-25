using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ProgressBar;

namespace ChatApp.Shared
{
    // --- 1. CÁC LOẠI GÓI TIN ---
    public enum PacketType
    {
        Auth,           // Đăng nhập
        Message,        // Tin nhắn văn bản
        Video,          // Hình ảnh Video Call
        File,           // Truyền File
        KeyExchange,    // Trao đổi khóa Diffie-Hellman
        Error           // Báo lỗi
    }

    // --- 2. CẤU TRÚC DỮ LIỆU (DTO) ---
    public class DataPacket
    {
        public PacketType Type { get; set; }

        // Người gửi
        public string Sender { get; set; } = "";

        // [MỚI] Người nhận: 
        // - Nếu để trống hoặc null: Gửi cho tất cả (Broadcast)
        // - Nếu có tên: Gửi riêng cho người đó (Private)
        public string Recipient { get; set; } = "";

        // Dùng cho Auth (Mật khẩu)
        public string Password { get; set; } = "";

        // Dùng cho File Transfer (Tên file)
        public string FileName { get; set; } = "";

        // Payload chính (Chứa Text mã hóa, Ảnh Video, File, hoặc Public Key)
        public byte[]? Data { get; set; }

        // Vector khởi tạo (IV) bắt buộc cho AES
        public byte[]? IV { get; set; }
    }

    // --- 3. BỘ MÃ HÓA AES (CƠ CHẾ HYBRID: DH + DEFAULT) ---
    public static class SimpleAES
    {
        // Khóa dự phòng (Dùng cho Chat nhóm, File và Video công khai)
        // Đảm bảo ai cũng có thể giải mã được nếu không có khóa riêng tư
        public static readonly byte[] DefaultKey = Encoding.UTF8.GetBytes("12345678901234567890123456789012");

        // --- A. MÃ HÓA CHUỖI (TEXT) ---
        // --- ĐƯỢC GỌI TỪ: Hàm SendMsg() trong Form1.cs (Client) trước khi gửi tin đi ---
        public static byte[] EncryptString(string text, byte[] key, out byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                // Nếu key null thì dùng DefaultKey
                aes.Key = key ?? DefaultKey;
                // Tạo mã xáo trộn (IV): Giúp cùng 1 nội dung nhưng mỗi lần mã hóa ra kết quả khác nhau
                aes.GenerateIV();
                //Xuất IV ra để gửi kèm gói tin cho bên nhận có cái mà giải mã
                iv = aes.IV;
                //Định dạng chuẩn: Dùng PKCS7 để lấp đầy các ô dữ liệu còn trống
                aes.Padding = PaddingMode.PKCS7; // Chuẩn Padding quốc tế
                //Dùng 'encryptor' để biến đổi chuỗi văn bản (text) thành mảng byte xáo trộn qua luồng CryptoStream
                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    using (var sw = new StreamWriter(cs)) { sw.Write(text); }
                    //Kết quả: Trả về mảng byte đã được "khóa" an toàn
                    return ms.ToArray();
                }
            }
        }

        // --- GIẢI MÃ CHUỖI (CÓ CƠ CHẾ TỰ SỬA LỖI PADDING) ---
        // --- ĐƯỢC GỌI TỪ: Hàm ReceiveLoop() trong Form1.cs (Client) khi nhận được gói tin nhắn từ mạng ---
        public static string DecryptString(byte[] cipherText, byte[] key, byte[] iv)
        {
            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = key ?? DefaultKey;
                    //Thiết lập IV: Phải dùng ĐÚNG mã xáo trộn mà bên gửi đã cung cấp kèm theo gói tin
                    aes.IV = iv;
                    //Định dạng chuẩn: Phải khớp với chuẩn PKCS7 lúc mã hóa thì mới gỡ được dữ liệu
                    aes.Padding = PaddingMode.PKCS7;
                    //Quy trình giải mã: 
                    // - Dùng 'decryptor' để đảo ngược quá trình xáo trộn.
                    // - Đọc dữ liệu từ mảng byte (cipherText) qua CryptoStream để đưa về dạng văn bản thuần túy.
                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    using (var ms = new MemoryStream(cipherText))
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    using (var sr = new StreamReader(cs))
                    {
                        //Trả về chuỗi tin nhắn đọc được
                        return sr.ReadToEnd();
                    }
                }
            }
            catch
            {
                // QUAN TRỌNG: Nếu giải mã bằng khóa DH thất bại (thường do chat nhóm)
                // -> Thử giải mã lại bằng DefaultKey
                if (key != DefaultKey)
                {
                    return DecryptString(cipherText, DefaultKey, iv);
                }
                // Nếu cả 2 chìa khóa đều không mở được thì báo lỗi để tránh treo ứng dụng
                return "[Tin nhắn lỗi mã hóa - Không thể đọc]";
            }
        }

        // --- B. MÃ HÓA FILE / VIDEO (BYTE ARRAY) ---
        // --- ĐƯỢC GỌI TỪ: Hàm SendFile() hoặc Camera_NewFrame() trong Form1.cs (Client) ---
        public static byte[] EncryptBytes(byte[] originalData, byte[] key, out byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key ?? DefaultKey;
                //Tạo mã xáo trộn (IV): Đảm bảo các khối dữ liệu giống nhau của file không bị trùng lặp khi mã hóa
                aes.GenerateIV();
                iv = aes.IV;
                //Định dạng chuẩn: Lấp đầy dữ liệu theo chuẩn PKCS7
                aes.Padding = PaddingMode.PKCS7;
                //Quy trình mã hóa trực tiếp:
                // Khác với hàm String (dùng Stream), hàm này dùng 'TransformFinalBlock' 
                // đổi toàn bộ mảng byte dữ liệu gốc thành mảng byte đã mã hóa ngay lập tức.
                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                {
                    //Trả về dữ liệu file / video đã được mã hóa an toàn
                    return encryptor.TransformFinalBlock(originalData, 0, originalData.Length);
                }
            }
        }
        // --- ĐƯỢC GỌI TỪ: Hàm ReceiveLoop() trong Form1.cs (Client) khi nhận gói tin File hoặc Video ---
        public static byte[] DecryptBytes(byte[] cipherData, byte[] key, byte[] iv)
        {
            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = key ?? DefaultKey;
                    aes.IV = iv;
                    aes.Padding = PaddingMode.PKCS7;
                    //Quy trình giải mã trực tiếp:
                    // Dùng 'decryptor' để biến mảng byte đã mã hóa (cipherData) trở về mảng byte gốc.
                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    {
                        //Trả về dữ liệu file hoặc khung hình video đã giải mã thành công
                        return decryptor.TransformFinalBlock(cipherData, 0, cipherData.Length);
                    }
                }
            }
            catch
            {
                // Thử lại với DefaultKey nếu lỗi (Cơ chế fallback)
                // Nếu dùng khóa riêng bị lỗi (do lệch khóa hoặc là file gửi chung cho phòng)
                if (key != DefaultKey) return DecryptBytes(cipherData, DefaultKey, iv);

                // Trả về rỗng nếu bó tay (để không crash app)
                return new byte[0];
            }
        }
    }
}