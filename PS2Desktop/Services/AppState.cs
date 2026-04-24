using PS2Desktop.Modelos;

namespace PS2Desktop.Services
{
    // Simple global app state holder. For a larger app use DI/IoC.
    public static class AppState
    {
        public static PostgresService Db { get; set; }
        public static User CurrentUser { get; set; }
    }
}
