namespace Client
{
    public static class Protocol
    {
        public const int Port = 5000;
        public const int NonceSize = 32;          // bytes
        public const int UserBlock = 64;          // padded username
        public const int HmacBlock = 32;          // HMAC-SHA256 output
        public const int FileNameBlock = 256;     // padded filename
        public const string AuthOk = "AUTH_OK"; 
        public const string AuthFail = "AUTH_FAIL";
    }
} 