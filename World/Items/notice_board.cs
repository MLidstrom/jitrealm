using System.Collections.Generic;
using System.Text;
using JitRealm.Mud;

/// <summary>
/// A village notice board in the post office with announcements and news.
/// </summary>
public sealed class NoticeBoard : SignBase
{
    public override string Name => "a notice board";
    public override string ReadableLabel => "notice board";
    public override IReadOnlyList<string> Aliases => new[]
    {
        "board", "notice board", "notices", "announcements", "bulletin", "bulletin board"
    };

    public override string ReadableText
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== MILLBROOK NOTICE BOARD ===");
            sb.AppendLine();
            sb.AppendLine("OFFICIAL NOTICES:");
            sb.AppendLine("  * The village council meets on the first day of each month.");
            sb.AppendLine("  * All mail must be properly stamped. See postmaster for rates.");
            sb.AppendLine("  * The watermill is operating. Grain may be brought for milling.");
            sb.AppendLine();
            sb.AppendLine("LOCAL NEWS:");
            sb.AppendLine("  * Greta Ironhand has new iron weapons in stock!");
            sb.AppendLine("  * The Sleepy Dragon serves the best meat pies in the region.");
            sb.AppendLine("  * WANTED: Adventurers to clear goblins from the northern meadow.");
            sb.AppendLine();
            sb.AppendLine("LOST & FOUND:");
            sb.AppendLine("  * Found: One grey cat. Very friendly. Answers to nothing.");
            sb.AppendLine();
            sb.AppendLine("    - Posted by Order of the Millbrook Council");

            return sb.ToString().TrimEnd();
        }
    }
}
