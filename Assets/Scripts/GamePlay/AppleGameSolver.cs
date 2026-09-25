using UnityEngine;

/// <summary>
/// 숫자 배열에서 합이 10인 사각형을 탐색하고, 힌트와 탐욕적 제거 시뮬레이션을 제공한다.
/// 화면 객체, 점수 UI, 네트워크 상태를 직접 변경하지 않는 동기 계산 클래스이다.
/// 입력은 ROW × COLUMN 크기의 행 우선 배열이며 index = row * COLUMN + column이다.
/// 값은 0(빈칸) 또는 1~9(사과)를 전제로 한다. null, 길이, 값 범위는 여기서 검사하지 않는다.
/// 계산 중 입력 배열이 다른 곳에서 변경되지 않아야 한다.
/// </summary>
public class AppleGameSolver
{
    /// <summary>
    /// 탐색 결과와 힌트 우선순위 비교 정보를 담는다.
    /// 행 [r1, r2], 열 [c1, c2] 모두 끝 인덱스를 포함한다.
    /// 서버 선택 요청의 끝 제외 범위와 다르므로 전송에 사용하려면 끝 경계를 변환해야 한다.
    /// isValid가 false이면 나머지 필드를 유효한 선택 영역으로 사용하지 않는다.
    /// </summary>
    public struct RectData
    {
        // (r1, c1)은 좌상단, (r2, c2)는 우하단의 보드 인덱스이다.
        public int r1, r2, c1, c2;
        // 영역 내 가장 큰 사과 값. 후보 비교에서 클수록 우선한다.
        public int maxNum;
        // 빈칸까지 포함하는 사각형의 칸 수. maxNum이 같으면 작을수록 우선한다.
        // 실제 제거되는 사과 개수나 획득 점수와 같다고 볼 수 없다.
        public int area;
        // 합이 10인 영역을 찾았는지 나타낸다. 보드 전체를 해결할 수 있다는 뜻은 아니다.
        public bool isValid;
    }

    /// <summary>
    /// GetHint가 고른 사각형을 반복 제거했을 때 얻는 추가 점수를 계산한다.
    /// 보드 복사본만 변경하므로 원본 보드와 화면에는 영향을 주지 않는다.
    /// 모든 제거 순서를 비교하는 최적화가 아니라 한 가지 탐욕적 경로를 시뮬레이션한다.
    /// 따라서 실제 최대 점수를 보장하지 않는다. 유효한 입력과 시간 제한 없는 규칙에서는
    /// 가능한 제거 경로 하나의 점수이며, 최적 점수보다 작거나 같을 수 있다.
    /// 플레이 시간이나 사람의 입력 속도는 시뮬레이션하지 않는다.
    /// </summary>
    public static int GetEstimatedMaxScore(int[] originalGrid)
    {
        // int 배열은 Clone으로 요소 값이 복사되므로 simGrid를 수정해도 원본은 바뀌지 않는다.
        int[] simGrid = (int[])originalGrid.Clone();

        int totalRemoved = 0;

        while (true)
        {
            // 제거 후에는 가능한 영역이 바뀌므로 매번 새 보드로 누적 합과 후보를 다시 계산한다.
            // 합이 10인 영역은 반드시 양수 칸을 포함하므로 매 성공마다 사과 수가 줄어든다.
            // 정상 입력에서는 유한 번 제거한 뒤 탐색이 종료된다.
            RectData bestRect = GetHint(simGrid);

            if (!bestRect.isValid)
            {
                break;
            }

            for (int r = bestRect.r1; r <= bestRect.r2; r++)
            {
                for (int c = bestRect.c1; c <= bestRect.c2; c++)
                {
                    // 빈칸은 점수에 포함하지 않는다. 영역 넓이가 아니라 제거한 사과 개수를 누적한다.
                    if (simGrid[r * GameConstants.COLUMN + c] != 0)
                    {
                        totalRemoved++;
                        simGrid[r * GameConstants.COLUMN + c] = 0;
                    }
                }
            }
        }

        return totalRemoved;
    }

    /// <summary>
    /// 현재 보드에 합이 10인 사각형이 하나라도 있으면 true를 반환한다.
    /// 더 이상 가능한 선택이 없는 상태를 검사하며, 스레드의 교착 상태 검사가 아니다.
    /// 힌트의 우선순위를 비교할 필요가 없으므로 첫 유효 영역을 찾자마자 종료한다.
    /// R=ROW, C=COLUMN일 때 최악의 시간은 O(R²C²), 누적 합 공간은 O(RC)이다.
    /// </summary>
    public static bool HasAvailableMoves(int[] grid)
    {
        int[,] pSum = BuildPrefixSum(grid);

        for (int r1 = 0; r1 < GameConstants.ROW; r1++)
        {
            for (int c1 = 0; c1 < GameConstants.COLUMN; c1++)
            {
                for (int r2 = r1; r2 < GameConstants.ROW; r2++)
                {
                    for (int c2 = c1; c2 < GameConstants.COLUMN; c2++)
                    {
                        int sum = GetSum(pSum, r1, r2, c1, c2);
                        // 고정한 r1, c1, r2에서 오른쪽 끝 c2만 늘리는 중이다.
                        // 모든 값이 0 이상이므로 합은 줄지 않는다. 10을 넘으면 이 c2 탐색만 중단한다.
                        // 음수 값을 허용하는 규칙으로 바꾸면 이 가지치기는 더 이상 올바르지 않다.
                        if (sum > 10)
                            break;
                        if (sum == 10)
                            return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 합이 10인 후보 중 maxNum이 가장 큰 영역, 다음으로 area가 가장 작은 영역을 고른다.
    /// 두 값까지 같으면 반복문에서 먼저 발견한 후보를 유지하므로 결과가 순회 순서에 의존한다.
    /// 이 우선순위는 휴리스틱이며 즉시 획득 점수나 최종 점수를 최대화한다는 보장은 없다.
    /// 합 조회는 O(1)이지만 유효 후보마다 최대값 탐색을 추가 수행한다.
    /// 후보 수 O(R²C²), 후보당 최대값 탐색 O(RC)로 보수적 최악 시간 상한은 O(R³C³)이다.
    /// </summary>
    public static RectData GetHint(int[] grid)
    {
        int[,] pSum = BuildPrefixSum(grid);

        // isValid로 후보 유무를 구분한다. -1은 아직 평가하지 않았다는 초기 표시값이다.
        RectData bestRect = new RectData { isValid = false, maxNum = -1, area = -1 };

        for (int r1 = 0; r1 < GameConstants.ROW; r1++)
        {
            for (int c1 = 0; c1 < GameConstants.COLUMN; c1++)
            {
                for (int r2 = r1; r2 < GameConstants.ROW; r2++)
                {
                    for (int c2 = c1; c2 < GameConstants.COLUMN; c2++)
                    {
                        int sum = GetSum(pSum, r1, r2, c1, c2);
                        // 고정한 r1, c1, r2에서 오른쪽 끝 c2만 늘리는 중이다.
                        // 모든 값이 0 이상이므로 합은 줄지 않는다. 10을 넘으면 이 c2 탐색만 중단한다.
                        // 음수 값을 허용하는 규칙으로 바꾸면 이 가지치기는 더 이상 올바르지 않다.
                        if (sum > 10)
                            break;
                        // 합이 10이어도 탐색을 계속한다. 같은 시작점의 확장은 넓이가 커지지만,
                        // 다른 시작점/행 범위에는 더 우선하는 후보가 있을 수 있다.
                        if (sum == 10)
                        {
                            int maxNum = GetMaxNumInRect(grid, r1, r2, c1, c2);
                            // 양 끝을 포함하므로 각 길이에 1을 더한다. 빈칸도 넓이에 포함한다.
                            int area = (r2 - r1 + 1) * (c2 - c1 + 1);
                            // 우선순위: 첫 후보 → 더 큰 최대 숫자 → 최대 숫자가 같으면 더 작은 넓이.
                            // 넓이까지 같은 후보에는 교체 조건이 없어 기존 후보를 유지한다.
                            if (!bestRect.isValid || maxNum > bestRect.maxNum || (maxNum == bestRect.maxNum && area < bestRect.area))
                            {
                                bestRect.r1 = r1;
                                bestRect.r2 = r2;
                                bestRect.c1 = c1;
                                bestRect.c2 = c2;
                                bestRect.maxNum = maxNum;
                                bestRect.area = area;
                                bestRect.isValid = true;
                            }
                        }
                    }
                }
            }
        }

        return bestRect;
    }

    /// <summary>
    /// pSum[r, c]에 원본 행 [0, r), 열 [0, c)의 합을 저장한다.
    /// 맨 위 행과 맨 왼쪽 열을 0으로 비워 두어 경계에서도 같은 식을 사용할 수 있다.
    /// 생성 시간과 추가 공간은 O(ROW × COLUMN)이다.
    /// </summary>
    private static int[,] BuildPrefixSum(int[] grid)
    {
        int[,] pSum = new int[GameConstants.ROW + 1, GameConstants.COLUMN + 1];

        for (int r = 1; r <= GameConstants.ROW; r++)
        {
            for (int c = 1; c <= GameConstants.COLUMN; c++)
            {
                // 현재 값 + 위쪽 누적 합 + 왼쪽 누적 합 - 두 영역에 중복 포함된 좌상단 합.
                // 누적 합 배열은 1부터 채우므로 원본에는 r-1, c-1로 접근한다.
                pSum[r, c] = grid[(r - 1) * GameConstants.COLUMN + (c - 1)] + pSum[r - 1, c] + pSum[r, c - 1] - pSum[r - 1, c - 1];
            }
        }

        return pSum;
    }

    /// <summary>
    /// 끝을 포함하는 사각형 [r1, r2] × [c1, c2]의 합을 O(1)에 구한다.
    /// 전체 누적 영역에서 위/왼쪽을 빼고, 두 번 빠진 좌상단을 다시 더한다.
    /// 인덱스의 유효성과 pSum이 해당 보드에서 만들어졌는지는 호출자가 보장한다.
    /// </summary>
    private static int GetSum(int[,] pSum, int r1, int r2, int c1, int c2)
    {
        // 원본의 포함 끝 인덱스를 누적 합의 제외 끝 경계로 바꾸므로 r2+1, c2+1을 사용한다.
        return pSum[r2 + 1, c2 + 1] - pSum[r1, c2 + 1] - pSum[r2 + 1, c1] + pSum[r1, c1];
    }

    /// <summary>
    /// 끝을 포함한 사각형의 칸들을 직접 방문하여 최대 숫자를 구한다. 시간은 영역 넓이에 비례한다.
    /// 합을 위한 누적 합으로는 최대값을 같은 방식으로 구할 수 없어 별도로 탐색한다.
    /// 정상 입력이 음수가 아니므로 초기값은 0이며, 빈칸만 있는 영역의 결과도 0이다.
    /// </summary>
    private static int GetMaxNumInRect(int[] grid, int r1, int r2, int c1, int c2)
    {
        int maxNum = 0;
        for (int r = r1; r <= r2; r++)
        {
            for (int c = c1; c <= c2; c++)
            {
                if (grid[r * GameConstants.COLUMN + c] > maxNum)
                {
                    maxNum = grid[r * GameConstants.COLUMN + c];
                }
            }
        }

        return maxNum;
    }
}
