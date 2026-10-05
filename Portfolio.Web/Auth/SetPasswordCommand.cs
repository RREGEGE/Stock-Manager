using System.Text;

namespace Portfolio.Web.Auth;

// `Portfolio.Web set-password`: 비밀번호를 잊었을 때 앱을 실행하는 PC의 터미널에서 다시 정한다.
// 평소 가입·비밀번호 변경은 웹 화면에서 한다. 이 명령은 현재 비밀번호를 묻지 않으므로 이 PC에서만 쓸 수 있게 둔다.
public static class SetPasswordCommand
{
    public const string Name = "set-password";

    public static async Task<int> RunAsync(AccountService accounts, Func<string, string?> prompt, TextWriter output)
    {
        if (!await accounts.IsRegisteredAsync())
        {
            output.WriteLine("아직 계정이 없습니다. 앱을 켜고 웹 화면에서 가입하세요.");
            return 1;
        }

        output.WriteLine($"'{await accounts.GetUserNameAsync()}' 계정의 비밀번호를 다시 정합니다.");
        string? first = prompt($"새 비밀번호 ({AuthOptions.MinPasswordLength}자 이상): ");
        string? second = prompt("한 번 더 입력: ");
        if (first != second)
        {
            output.WriteLine("두 입력이 서로 다릅니다. 변경하지 않았습니다.");
            return 1;
        }

        try
        {
            await accounts.ResetPasswordAsync(first);
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
