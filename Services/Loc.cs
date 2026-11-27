using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MSharpPro.Services;

/// <summary>
/// Basit iki dilli arayüz (Türkçe / İngilizce). Kaynak metinler Türkçe yazılır;
/// İngilizce seçiliyse <see cref="L"/> ve <see cref="Translate"/> bunları sözlükten çevirir.
/// Dil açılışta belirlenir (değiştirmek uygulamayı yeniden başlatır).
/// </summary>
public static partial class Loc
{
    public static bool English { get; private set; }

    public static void Init(string setting)
    {
        English = setting switch
        {
            "en" => true,
            "tr" => false,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "tr",
        };
    }

    /// <summary>Türkçe metni seçili dile çevirir (sözlükte yoksa olduğu gibi döner).</summary>
    public static string L(string tr)
    {
        if (!English || string.IsNullOrEmpty(tr)) return tr;
        if (En.TryGetValue(tr, out var en)) return en;
        // XAML'deki satır içi metinlerde baştaki / sondaki boşlukları koru
        string t = tr.Trim();
        if (t.Length == tr.Length || !En.TryGetValue(t, out en)) return tr;
        int lead = tr.IndexOf(t, StringComparison.Ordinal);
        return tr[..lead] + en + tr[(lead + t.Length)..];
    }

    /// <summary>Biçimli metin: L("{0} ekran bağlı") gibi; çeviri sonrası string.Format uygulanır.</summary>
    public static string L(string tr, params object?[] args) => string.Format(L(tr), args);

    /// <summary>Bir pencere / öğe ağacındaki sabit metinleri (TextBlock, Run, içerik, ipucu) çevirir.</summary>
    public static void Translate(DependencyObject root)
    {
        if (!English) return;
        Walk(root, new HashSet<DependencyObject>());
    }

    private static void Walk(DependencyObject d, HashSet<DependencyObject> seen)
    {
        if (!seen.Add(d)) return;
        switch (d)
        {
            case TextBlock tb:
                if (tb.Inlines.Count > 1 || tb.Inlines.FirstOrDefault() is not Run)
                {
                    foreach (var run in tb.Inlines.OfType<Run>())
                        if (BindingOf(run, Run.TextProperty) == null) run.Text = L(run.Text);
                }
                else if (!string.IsNullOrEmpty(tb.Text) && BindingOf(tb, TextBlock.TextProperty) == null
                         && !(tb.Inlines.FirstOrDefault() is Run r0 && BindingOf(r0, Run.TextProperty) != null))
                {
                    tb.Text = L(tb.Text);
                }
                break;
            case ContentControl cc when cc.Content is string s:
                cc.Content = L(s);
                break;
        }
        if (d is FrameworkElement fe)
        {
            if (fe.ToolTip is string tip) fe.ToolTip = L(tip);
            if (fe.Tag is string tag && tag.Length > 1) fe.Tag = L(tag);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
            Walk(child, seen);
        if (d is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(d, i), seen);
        }
    }

    private static System.Windows.Data.BindingExpression? BindingOf(DependencyObject d, DependencyProperty p) =>
        System.Windows.Data.BindingOperations.GetBindingExpression(d, p);
}
