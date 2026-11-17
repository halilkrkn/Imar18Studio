using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>Komut cubugu girdisi. Kural tabanlidir; urun ici LLM yoktur (Faz 18 kilitli — security.md).</summary>
public sealed record PaletteCommand(string Id, string Title, string Keywords, Action Execute, Func<bool>? CanExecute = null);

/// <summary>
/// Ctrl+K komut cubugu (P4.3, UI referansi "Türkçe kural tabanlı komut çubuğu"). Arama Turkce karakter ve buyuk/kucuk
/// harf duyarsizdir; puan: baslik oneki (3) &gt; baslik kelime oneki (2) &gt; baslik icerir (1) / anahtar kelime icerir (1).
/// P7 cilası: listedeki komut fareyle tıklanınca / çift tıklanınca veya Enter'la gerçekten çalışır ve çubuk kapanır
/// (<see cref="Calistir"/>); çalıştırılamayan komut çubuğu kapatmaz, nedenini <see cref="Mesaj"/>'da gösterir. Esc veya dışarı
/// tıklama <see cref="KapatCommand"/> ile kapatır.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly List<PaletteCommand> _commands = [];

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private int _selectedIndex = -1;

    /// <summary>Son çalıştırma girişiminin kullanıcı mesajı (ör. komut şu anda kullanılamaz); boşsa gösterilmez.</summary>
    [ObservableProperty]
    private string _mesaj = string.Empty;

    public ObservableCollection<PaletteCommand> Results { get; } = [];

    public void Register(PaletteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Add(command);
        Refresh();
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
        Mesaj = string.Empty;
        if (!IsOpen)
        {
            Query = string.Empty;
        }
    }

    public void Close()
    {
        IsOpen = false;
        Query = string.Empty;
        Mesaj = string.Empty;
    }

    /// <summary>Esc / dışarı tıklama: çubuğu kapatır (açık değilse etkisiz).</summary>
    [RelayCommand]
    private void Kapat() => Close();

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Results.Count - 1);
    }

    /// <summary>Secili komutu calistirir ve cubugu kapatir; calistirilamazsa cubuk acik kalir ve neden yazilir.</summary>
    public void ExecuteSelected()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            Calistir(Results[SelectedIndex]);
        }
    }

    /// <summary>Komutu çalıştırır ve çubuğu kapatır (tıklama, çift tıklama, Enter). Çalıştırılamıyorsa false.</summary>
    public bool Calistir(PaletteCommand? komut)
    {
        if (komut is null)
        {
            return false;
        }

        if (komut.CanExecute is not null && !komut.CanExecute())
        {
            Mesaj = $"“{komut.Title}” şu anda kullanılamaz (ön koşulu sağlanmadı).";
            return false;
        }

        Close();
        komut.Execute();
        return true;
    }

    /// <summary>Listeden tıklanan komutu çalıştırır (görünüm: liste öğesi komutu).</summary>
    [RelayCommand]
    private void KomutuCalistir(PaletteCommand? komut) => Calistir(komut);

    partial void OnQueryChanged(string value)
    {
        Mesaj = string.Empty;
        Refresh();
    }

    private void Refresh()
    {
        var q = Normalize(Query);
        var ranked = _commands
            .Select(c => (Command: c, Score: Score(c, q)))
            .Where(x => q.Length == 0 || x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Command.Title, StringComparer.Ordinal)
            .Select(x => x.Command)
            .ToList();
        if (q.Length == 0)
        {
            ranked = [.. _commands];
        }

        Results.Clear();
        foreach (var c in ranked)
        {
            Results.Add(c);
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    private static int Score(PaletteCommand c, string q)
    {
        if (q.Length == 0)
        {
            return 1;
        }

        var title = Normalize(c.Title);
        if (title.StartsWith(q, StringComparison.Ordinal))
        {
            return 3;
        }

        if (title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.StartsWith(q, StringComparison.Ordinal)))
        {
            return 2;
        }

        return title.Contains(q, StringComparison.Ordinal) || Normalize(c.Keywords).Contains(q, StringComparison.Ordinal) ? 1 : 0;
    }

    /// <summary>Turkce harfleri ASCII karsiligina indirger ve kucuk harfe cevirir (ı/İ → i, ş → s ...).</summary>
    internal static string Normalize(string? text)
    {
        var sb = new StringBuilder((text ?? string.Empty).Length);
        foreach (var ch in (text ?? string.Empty).Trim())
        {
            sb.Append(ch switch
            {
                'ı' or 'İ' or 'I' => 'i',
                'ş' or 'Ş' => 's',
                'ğ' or 'Ğ' => 'g',
                'ü' or 'Ü' => 'u',
                'ö' or 'Ö' => 'o',
                'ç' or 'Ç' => 'c',
                _ => char.ToLowerInvariant(ch),
            });
        }

        return sb.ToString();
    }
}
