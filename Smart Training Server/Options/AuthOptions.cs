namespace Smart_Training_Server.Options
{
    public class AuthOptions
    {
        public int AccessTokenLifetimeMinutes { get; set; } = 60;
        public int SessionGraceMinutes { get; set; } = 5;

        public int NameMinLength { get; set; }

        public int NameMaxLength { get; set; }

        public bool AllowDuplicateName { get; set; }
    }
}
