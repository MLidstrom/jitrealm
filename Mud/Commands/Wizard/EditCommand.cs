namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// ANSI-based file editor command (nano-style).
/// </summary>
public class EditCommand : WizardCommandBase
{
    public override string Name => "edit";
    public override string Usage => "edit <file>";
    public override string Description => "Edit a file (nano-style editor)";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            context.Output("Usage: edit <file>");
            return;
        }

        // Check ANSI support
        if (!context.Session.SupportsAnsi)
        {
            context.Output("The editor requires ANSI terminal support.");
            return;
        }

        var worldRoot = WizardFilesystem.GetWorldRoot(context);
        var sessionId = context.Session.SessionId;

        var filePath = string.Join(" ", args);
        var resolvedPath = WizardFilesystem.ResolvePath(sessionId, filePath, worldRoot);

        if (resolvedPath is null)
        {
            context.Output("Invalid path.");
            return;
        }

        var fsPath = WizardFilesystem.ToFilesystemPath(resolvedPath, worldRoot);

        // Load existing content or start with empty file
        string[] content;
        if (File.Exists(fsPath))
        {
            content = await File.ReadAllLinesAsync(fsPath);
        }
        else
        {
            // Ensure parent directory exists for new files
            var dir = Path.GetDirectoryName(fsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                context.Output($"Directory does not exist: {Path.GetDirectoryName(resolvedPath)}");
                return;
            }
            content = Array.Empty<string>();
        }

        context.Output($"Opening {resolvedPath}...");

        // Create and run the editor
        var editor = new TextEditor(context.Session, fsPath, resolvedPath, content);

        // Create character reading function
        Func<Task<char?>> readChar = () => context.Session.ReadCharAsync();

        var saved = await editor.RunAsync(readChar);

        if (saved)
        {
            context.Output($"Saved {resolvedPath}");
        }
        else
        {
            context.Output("Editor closed.");
        }
    }
}
