namespace GUI
{
    public static class Protocol
    {
        public const int Port = 5000;
        public const int NonceSize = 32;
        public const int UserBlock = 64;
        public const int HmacBlock = 32;
        public const int FileNameBlock = 256;
        public const string AuthOk = "AUTH_OK";
        public const string AuthFail = "AUTH_FAIL";
    }
} 