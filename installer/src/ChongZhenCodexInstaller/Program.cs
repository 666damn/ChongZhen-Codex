namespace ChongZhenCodexInstaller;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var options = CommandLine.Parse(args);
        if (options.Command != Domain.AppCommand.UserInterface) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }
}
