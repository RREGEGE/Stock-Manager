using System.Text;

namespace Portfolio.Web.Auth;

// `Portfolio.Web set-password`: 이 PC의 터미널에서만 비밀번호를 설정·변경한다.
// 웹 화면에는 설정 기능을 두지 않는다 (다른 기기에서 먼저 비밀번호를 정해 버리는 일을 막기 위함).
public static class SetPasswordCommand
{
    public const string Name = "set-password";

    public static async Task<int> RunAsync(PasswordService passwords, Func<string, string?> prompt, TextWriter output)
    {
        output.WriteLine(await passwords.IsConfiguredAsync()
            ? "로그인 비밀번호를 변경합니다."
            : "로그인 비밀번호를 설정합니다.");

        string? first = prompt($"새 비밀번호 ({AuthOptions.MinPasswordLength}자 이상): ");
        string? second = prompt("한 번 더 입력: ");
        if (first != second)
        {
            output.WriteLine("두 입력이 서로 다릅니다. 변경하지 않았습니다.");
            return 1;
        }

        try
        {
            await passwords.SetAsync(first ?? "");
        }
        catch (ArgumentException ex)
        {
            output.WriteLine(ex.Message + " 변경하지 않았습니다.");
            return 1;
        }

        output.WriteLine("비밀번호를 저장했습니다. 이전에 로그인한 기기는 모두 새 비밀번호로 다시 로그인해야 합니다.");
        return 0;
    }

    // 입력한 글자를 화면에 보이지 않게 읽는다
    public static string? ReadHidden(string label)
    {
        Console.Write(label);
        if (Console.IsInputRedirected)
            return Console.ReadLine();

        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) sb.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
            }
        }
        Console.WriteLine();
        return sb.ToString();
    }
}
