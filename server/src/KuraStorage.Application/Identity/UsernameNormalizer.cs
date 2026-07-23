using System.Text;

namespace KuraStorage.Application.Identity;

public static class UsernameNormalizer
{
    public static string Normalize(string username) =>
        username.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
}
