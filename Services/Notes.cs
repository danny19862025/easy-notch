using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace EasyNotch.Services;

public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    /// <summary>Plain text; lines starting with "[ ]" or "[x]" show as checkboxes.</summary>
    public string Body { get; set; } = "";
    public bool Pinned { get; set; }
    public DateTime Updated { get; set; } = DateTime.Now;

    public string DisplayTitle => Title.Length > 0 ? Title : Body.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "New note";

    public IEnumerable<string> Lines => Body.Replace("\r", "").Split('\n');

    /// <summary>Fraction of checklist items ticked, or null when the note has no checklist.</summary>
    public double? Progress
    {
        get
        {
            var items = Lines.Where(IsCheckLine).ToList();
            return items.Count == 0 ? null : items.Count(l => IsChecked(l)) / (double)items.Count;
        }
    }

    public static bool IsCheckLine(string l) => l.TrimStart().StartsWith("[ ]") || IsChecked(l);
    public static bool IsChecked(string l) => l.TrimStart().StartsWith("[x]", StringComparison.OrdinalIgnoreCase);
    public static string CheckText(string l) => l.TrimStart()[3..].TrimStart();
}

/// <summary>Notes kept in %LOCALAPPDATA%\EasyNotch\notes.json, saved on every change.</summary>
public sealed class NotesStore
{
    static string FilePath => Path.Combine(Settings.Dir, "notes.json");
    static string OldFilePath => Path.Combine(Settings.Dir, "notes.txt");

    public ObservableCollection<Note> Items { get; } = [];
    public event Action? Changed;

    public NotesStore()
    {
        try
        {
            foreach (var n in JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(FilePath)) ?? []) Items.Add(n);
        }
        catch { }

        // Carry over the single scratchpad from earlier versions.
        if (Items.Count == 0 && File.Exists(OldFilePath))
        {
            try
            {
                string old = File.ReadAllText(OldFilePath);
                if (old.Trim().Length > 0) { Items.Add(new Note { Title = "Notes", Body = old }); Save(); }
                File.Move(OldFilePath, OldFilePath + ".bak", overwrite: true);
            }
            catch { }
        }
        Sort();
    }

    public Note Add()
    {
        var n = new Note();
        Items.Insert(Items.TakeWhile(i => i.Pinned).Count(), n);
        Save();
        return n;
    }

    public void Update(Note n, string title, string body)
    {
        if (n.Title == title && n.Body == body) return;
        n.Title = title;
        n.Body = body;
        n.Updated = DateTime.Now;
        Save();
    }

    public void ToggleCheck(Note n, int lineIndex)
    {
        var lines = n.Lines.ToList();
        if (lineIndex < 0 || lineIndex >= lines.Count || !Note.IsCheckLine(lines[lineIndex])) return;
        string l = lines[lineIndex];
        int at = l.IndexOf('[');
        lines[lineIndex] = l[..at] + (Note.IsChecked(l) ? "[ ]" : "[x]") + l[(at + 3)..];
        Update(n, n.Title, string.Join("\n", lines));
    }

    public void TogglePin(Note n)
    {
        n.Pinned = !n.Pinned;
        Sort();
        Save();
    }

    public void Remove(Note n)
    {
        Items.Remove(n);
        Save();
    }

    /// <summary>Drops notes left completely empty (e.g. "+" then back).</summary>
    public void Prune(Note n)
    {
        if (n.Title.Trim().Length == 0 && n.Body.Trim().Length == 0) Remove(n);
    }

    void Sort()
    {
        var sorted = Items.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.Updated).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int cur = Items.IndexOf(sorted[i]);
            if (cur != i) Items.Move(cur, i);
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Items.ToList()));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { }
        Changed?.Invoke();
    }
}

public static class TimeAgo
{
    public static string Of(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays}d ago";
        return t.ToString("d MMM");
    }
}
