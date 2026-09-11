using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;

public class AppleGameSolver
{
    public struct RectData
    {
        public int r1, r2, c1, c2;  // 좌측 상단 및 우측 하단 좌표 
        public int maxNum;          // 영역 내 포함된 가장 큰 숫자 (우선순위 1) 
        public int area;            // 영역의 넓이 (우선순위 2) 
        public bool isValid;        // 유효한 해답을 찾았는지 여부 
    }
    
    // 최고점 근삿값 예측. 주어진 보드를 복사하여 끝까지 시뮬레이션하고, 제거 가능한 최대 사과 개수를 반환. 
    public static int GetEstimatedMaxScore(int[] originalGrid)
    {
        int[] simGrid = (int[])originalGrid.Clone();

        int totalRemoved = 0; 

        while (true)
        {
            RectData bestRect = GetHint(simGrid); 

            if (!bestRect.isValid)
            {
                break; 
            }

            for (int r = bestRect.r1; r <= bestRect.r2; r++)
            {
                for (int c = bestRect.c1; c <= bestRect.c2; c++)
                {
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

    // 데드락 감지. 현재 보드에서 합이 10이 되는 영역이 단 하나라도 있는지 검사. 
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
                        if (sum > 10) break; 
                        if (sum == 10) return true;
                    }
                }
            }
        }

        return false; 
    }

    // 가장 큰 수를 포함한 넓이가 작은 영역을 우선적으로 제거하는 전략으로 합이 10이 되는 영역을 찾음.
    public static RectData GetHint(int[] grid)
    {
        int[,] pSum = BuildPrefixSum(grid); 

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
                        if (sum > 10) break;
                        if (sum == 10)
                        {
                            int maxNum = GetMaxNumInRect(grid, r1, r2, c1, c2);
                            int area = (r2 - r1 + 1) * (c2 - c1 + 1);
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


    private static int[,] BuildPrefixSum(int[] grid)
    {
        int[,] pSum = new int[GameConstants.ROW + 1, GameConstants.COLUMN + 1]; 

        for (int r = 1; r <= GameConstants.ROW; r++)
        {
            for (int c = 1; c <= GameConstants.COLUMN; c++)
            {
                pSum[r, c] = grid[(r - 1) * GameConstants.COLUMN + (c - 1)] + pSum[r - 1, c] + pSum[r, c - 1] - pSum[r - 1, c - 1];
            }
        }

        return pSum; 
    }

    private static int GetSum(int[, ] pSum, int r1, int r2, int c1, int c2)
    {
        return pSum[r2 + 1, c2 + 1] - pSum[r1, c2 + 1] - pSum[r2 + 1, c1] + pSum[r1, c1]; 
    }

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
