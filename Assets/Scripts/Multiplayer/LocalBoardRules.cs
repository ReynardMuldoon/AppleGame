using System;
using System.Collections.Generic;

// 오프라인 싱글플레이용 초기 보드 생성 규칙. 멀티플레이의 보드는 서버가 생성한다.
// 숫자 범위 1~9와 전체 합의 10 배수 조건을 맞추지만 완전 제거 가능성은 보장하지 않는다.
public static class LocalBoardRules
{
    // 전달받은 난수 생성기로 보드를 만들고 필요하면 한 칸 또는 두 칸을 보정한다.
    // 검토: random은 null이 아니어야 하며 현재 두 칸 보정은 보드가 최소 2칸임을 전제로 한다.
    // 보드 크기를 설정 가능하게 바꾸면 크기 검증을 추가해야 한다.
    // 보정 때문에 모든 유효 보드가 균등한 확률로 생성된다고 볼 수 없다.
    public static byte[] Generate(Random random)
    {
        // 행 우선 배열. Next(1, 10)은 1 이상 10 미만이므로 1~9를 생성한다.
        var b = new byte[GameConstants.ROW * GameConstants.COLUMN];
        int sum = 0;
        for (int i = 0; i < b.Length; i++)
        {
            b[i] = (byte)random.Next(1, 10);
            sum += b[i];
        }
        // 이미 조건을 만족하면 보정하지 않는다.
        if (sum % 10 == 0)
            return b;
        // 이 칸을 제외한 합에 더해서 10의 배수가 되는 나머지(0~9)를 계산한다.
        int at = random.Next(b.Length);
        int target = (10 - (sum - b[at]) % 10) % 10;
        // 필요한 값이 1~9이면 한 칸만 바꿔서 종료한다.
        if (target != 0)
        {
            b[at] = (byte)target;
            return b;
        }
        // target=0이면 한 칸을 0 또는 10으로 만들어야 하므로 1~9 규칙과 충돌한다.
        // 무작정 다시 뽑는 무한 반복 대신 서로 다른 두 칸의 조합으로 해결한다.
        // 같은 인덱스가 뽑히면 다음 칸을 사용하므로 두 번째 칸 선택은 완전한 균등 분포가 아니다.
        int second = random.Next(b.Length);
        if (second == at)
            second = (second + 1) % b.Length;
        // 두 칸을 제외한 나머지 합. 교체할 두 값만으로 전체 합 조건을 맞춘다.
        int rest = sum - b[at] - b[second];
        var pairs = new List<(int, int)>();
        // 1~9의 순서 있는 쌍 81개를 검사한다. 합 2~18은 모든 나머지를 포함하므로
        // 정상적인 최소 2칸 보드에서는 가능한 쌍이 존재한다.
        for (int a = 1; a <= 9; a++)
            for (int c = 1; c <= 9; c++)
                if ((rest + a + c) % 10 == 0)
                    pairs.Add((a, c));
        // 열거한 유효한 쌍 중 하나를 선택한다. 재추첨 종료 여부에 의존하지 않는다.
        var pair = pairs[random.Next(pairs.Count)];
        b[at] = (byte)pair.Item1;
        b[second] = (byte)pair.Item2;
        return b;
    }
}
