using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보드의 숫자 데이터와 화면에 표시할 사과 객체를 함께 관리한다.
/// 배열 인덱스는 row * COLUMN + column이며, 좌상단부터 행 단위로 저장한다.
/// 제거된 칸은 숫자 0과 객체 null로 표현하며 낙하나 재생성은 하지 않는다.
/// 선택의 합 검증, 점수 계산, 게임 종료 판단은 호출자가 담당한다.
/// Unity 객체를 생성/변경하므로 메인 스레드에서 사용한다.
/// </summary>
public class BoardManager : MonoBehaviour
{
    // 보드 로컬 좌표에서 사과 중심 사이의 간격과 사과 객체의 로컬 배율.
    // appleSize는 스프라이트의 실제 월드 크기를 직접 지정하는 값은 아니다.
    public const float spacer = 0.8f;
    public const float appleSize = 0.7f;

    // 초기 보드의 추정 점수로 분류한다. 0이 가장 낮고 5가 가장 높은 난이도이다.
    public int difficultyLevel = 0;

    // Inspector에서 연결할 사과 프리팹. GenerateBoard 호출 전에 연결되어 있어야 한다.
    [SerializeField] private Apple applePrefab;

    // 같은 인덱스의 두 배열이 화면 객체와 숫자 데이터를 각각 나타낸다.
    private Apple[] appleGrid;
    private int[] appleArray;

    // 생성 시 솔버가 계산한 추정치. 수학적으로 증명된 최대 점수나 상한은 아니다.
    // 플레이 도중 사과가 제거되어도 이 값과 difficultyLevel을 재계산하지 않는다.
    private int estimatedMaxScore;

    // 다른 컴포넌트의 Start에서 보드를 사용하기 전에 저장 공간을 준비한다.
    void Awake()
    {
        appleArray = new int[GameConstants.ROW * GameConstants.COLUMN];
        appleGrid = new Apple[GameConstants.ROW * GameConstants.COLUMN];
    }

    /// <summary>
    /// 내부 숫자 배열의 참조를 그대로 반환한다. 복사본이 아니다.
    /// 호출자가 값을 변경하면 화면 객체와 불일치할 수 있으므로 조회 용도로 사용한다.
    /// </summary>
    public int[] GetAppleArray()
    {
        return appleArray;
    }

    /// <summary>
    /// 전달받은 초기 숫자 배열로 보드 전체를 생성하고 난이도를 추정한다.
    /// data는 서버 수신 데이터 또는 싱글플레이에서 로컬 생성한 데이터일 수 있다.
    /// 칸 수와 각 값의 1~9 범위만 검사하며, 총합이나 해결 가능성은 검사하지 않는다.
    /// </summary>
    public void GenerateBoard(byte[] data)
    {
        int count = GameConstants.ROW * GameConstants.COLUMN;
        if (data == null || data.Length != count)
            throw new System.ArgumentException("Invalid board size");
        if (appleGrid == null)
            appleGrid = new Apple[count];
        if (appleArray == null)
            appleArray = new int[count];

        // 기존 화면을 정리한다. 값 검사와 파괴가 같은 루프에 있으므로,
        // 뒤쪽에서 잘못된 값을 발견하면 앞쪽 객체는 이미 제거 요청된 상태일 수 있다.
        for (int k = 0; k < count; k++)
        {
            if (data[k] < 1 || data[k] > 9)
                throw new System.ArgumentException("Invalid apple value");
            if (appleGrid[k] != null)
                Destroy(appleGrid[k].gameObject);
            appleGrid[k] = null;
        }

        // 입력 byte 배열의 값을 내부 int 배열로 복사한다. 입력 배열 자체를 보관하지 않는다.
        for (int i = 0; i < GameConstants.ROW * GameConstants.COLUMN; i++)
        {
            appleArray[i] = (int)data[i];
        }

        for (int i = 0; i < GameConstants.ROW; i++)
        {
            // 모든 사과 중심의 중앙이 보드 로컬 원점이 되도록 좌상단 중심 위치를 계산한다.
            float startX = -(GameConstants.COLUMN - 1) / 2.0f * spacer;
            float startY = (GameConstants.ROW - 1) / 2.0f * spacer;

            Vector3 appleScale = new Vector3(appleSize, appleSize, 1);

            for (int j = 0; j < GameConstants.COLUMN; j++)
            {
                // 열은 오른쪽(+x), 행은 아래쪽(-y)으로 증가한다.
                Vector2 position = new Vector2(startX + j * spacer, startY - i * spacer);

                // 보드의 자식으로 생성하므로 부모의 이동/배율을 함께 따른다.
                Apple apple = Instantiate(applePrefab, this.transform);
                apple.transform.localScale = appleScale;
                apple.transform.localPosition = position;

                // 2차원 좌표를 1차원 인덱스로 바꾸어 두 배열의 대응을 유지한다.
                apple.SetValue(appleArray[i * GameConstants.COLUMN + j], i, j);
                apple.SetSelected(false);
                apple.SetHinted(false);
                appleGrid[i * GameConstants.COLUMN + j] = apple;
            }
        }

        // 추정 제거 개수가 적을수록 어려운 보드로 분류한다.
        // 임계값은 고정되어 있으므로 보드 크기를 바꾼다면 별도로 검토해야 한다.
        estimatedMaxScore = AppleGameSolver.GetEstimatedMaxScore(appleArray);

        if (estimatedMaxScore <= 90)
            difficultyLevel = 5;
        else if (estimatedMaxScore <= 95)
            difficultyLevel = 4;
        else if (estimatedMaxScore <= 100)
            difficultyLevel = 3;
        else if (estimatedMaxScore <= 105)
            difficultyLevel = 2;
        else if (estimatedMaxScore <= 110)
            difficultyLevel = 1;
        else
            difficultyLevel = 0;

        Debug.Log($"Estimated Max Score: {estimatedMaxScore}, Difficulty Level: {difficultyLevel}");
    }

    /// <summary>
    /// 월드 좌표의 드래그 두 점으로부터 아직 남아 있는 사과의 1차원 인덱스를 반환한다.
    /// 스프라이트 외곽 충돌이 아니라 사과 중심 간격으로 계산한 범위를 사용한다.
    /// 월드 축과 정렬되고 축이 뒤집히지 않은 일반적인 2D 보드 배치를 전제로 한다.
    /// </summary>
    public List<int> GetApplesInDraggedArea(Vector2 dragStartPos, Vector2 dragEndPos)
    {
        // 어느 방향으로 드래그해도 같은 월드 좌표 사각형을 얻는다.
        float minX = Mathf.Min(dragStartPos.x, dragEndPos.x);
        float maxX = Mathf.Max(dragStartPos.x, dragEndPos.x);

        float minY = Mathf.Min(dragStartPos.y, dragEndPos.y);
        float maxY = Mathf.Max(dragStartPos.y, dragEndPos.y);

        Vector2 leftTop = new Vector2(minX, maxY);
        Vector2 rightBot = new Vector2(maxX, minY);

        // 생성 시 사용한 로컬 좌표계로 변환한 뒤 그리드 위치 공식을 역산한다.
        // 두 모서리만 변환하므로 회전/반전된 보드의 일반적인 영역 선택까지 지원하지 않는다.
        leftTop = this.transform.InverseTransformPoint(leftTop);
        rightBot = this.transform.InverseTransformPoint(rightBot);

        (int r1, int c1) = GetAppleGridIndex(leftTop);
        (int r2, int c2) = GetAppleGridIndex(rightBot);

        List<int> appleIndices = new List<int>();

        // 시작 포함, 끝 제외: [r1, r2) × [c1, c2).
        // 끝 경계에는 ROW/COLUMN이 올 수 있지만 반복문에서 해당 인덱스는 접근하지 않는다.
        for (int i = r1; i < r2; i++)
        {
            for (int j = c1; j < c2; j++)
            {
                if (appleGrid[i * GameConstants.COLUMN + j] != null)
                {
                    appleIndices.Add(i * GameConstants.COLUMN + j);
                }
            }
        }

        return appleIndices;
    }

    /// <summary>
    /// 행 [r1, r2], 열 [c1, c2]의 남아 있는 사과를 반환한다. 끝 인덱스도 포함한다.
    /// 드래그/서버 선택의 끝 제외 범위와 다르므로 그대로 혼용하면 안 된다.
    /// 호출자는 유효한 행/열 범위를 전달해야 한다. 이 함수는 범위를 보정하지 않는다.
    /// 매개변수 순서는 행 시작, 행 끝, 열 시작, 열 끝이다.
    /// </summary>
    public List<int> GetApplesByIndex(int r1, int r2, int c1, int c2)
    {
        List<int> appleIndices = new List<int>();
        for (int r = r1; r <= r2; r++)
        {
            for (int c = c1; c <= c2; c++)
            {
                if (appleGrid[r * GameConstants.COLUMN + c] != null)
                {
                    appleIndices.Add(r * GameConstants.COLUMN + c);
                }
            }
        }
        return appleIndices;
    }

    /// <summary>
    /// 전달된 인덱스의 사과를 제거하고 숫자 데이터를 0으로 바꾼다.
    /// 합이 10인지 검사하거나 점수를 올리지 않으므로 판정 완료 후 호출해야 한다.
    /// 목록은 null이 아니어야 한다. 범위 밖 인덱스와 이미 제거된 사과는 건너뛴다.
    /// </summary>
    public void RemoveSelectedApples(List<int> selectedAppleIndices)
    {
        foreach (int index in selectedAppleIndices)
        {
            if (index < 0 || index >= appleGrid.Length || appleGrid[index] == null)
                continue;

            // 실제로 제거하는 사과마다 효과음을 한 번씩 요청한다.
            AudioManager.Instance?.PlaySFX(SFXType.ApplePop);
            Destroy(appleGrid[index].gameObject);

            // Destroy의 실제 파괴 시점과 별개로 참조와 숫자는 즉시 비운다.
            // 같은 인덱스가 목록에 중복되어 있어도 이후 순회에서는 건너뛴다.
            appleGrid[index] = null;
            appleArray[index] = 0;
        }
    }

    /// <summary>
    /// 드래그 선택 강조를 켜거나 끈다. 순위 UI에서 본인 행을 강조하는 기능과는 별개이다.
    /// null/빈 목록은 허용하지만, 목록에 든 인덱스의 범위는 호출자가 보장해야 한다.
    /// </summary>
    public void HighlightApples(List<int> appleIndices, bool highlight)
    {
        if (appleIndices == null || appleIndices.Count == 0)
        {
            return;
        }
        foreach (int index in appleIndices)
        {
            if (appleGrid[index] != null)
            {
                appleGrid[index].SetSelected(highlight);
            }
        }
    }

    /// <summary>
    /// 주어진 사과들의 힌트 표시를 켜거나 끈다. 힌트 영역을 직접 계산하지는 않는다.
    /// 멀티플레이에서 힌트를 허용할지는 호출자가 결정한다.
    /// null/빈 목록은 허용하지만 인덱스 범위는 검사하지 않는다.
    /// </summary>
    public void HintApples(List<int> appleIndices, bool highlight)
    {
        if (appleIndices == null || appleIndices.Count == 0)
        {
            return;
        }
        foreach (int index in appleIndices)
        {
            if (appleGrid[index] != null)
            {
                appleGrid[index].SetHinted(highlight);
            }
        }
    }

    /// <summary>
    /// 보드 로컬 좌표를 끝 제외 반복문에 사용할 행/열 경계로 변환한다.
    /// 반환값은 실제 사과 인덱스에 한정되지 않으며 ROW/COLUMN도 가능하다.
    /// 따라서 반환값으로 배열에 직접 접근해서는 안 된다.
    /// </summary>
    private (int rowIndex, int columnIndex) GetAppleGridIndex(Vector2 pos)
    {
        // 생성 위치를 역산하고 올림한다. 가장 가까운 사과를 고르는 반올림과는 다르다.
        // 중심과 정확히 겹치는 경계에서는 위/왼쪽은 포함하고 아래/오른쪽은 제외한다.
        int r = Mathf.CeilToInt(-pos.y / spacer + (GameConstants.ROW - 1) / 2.0f);
        int c = Mathf.CeilToInt(pos.x / spacer + (GameConstants.COLUMN - 1) / 2.0f);

        // 배열의 마지막 인덱스가 아닌 '마지막 칸 다음 경계'까지 허용한다.
        r = Mathf.Clamp(r, 0, GameConstants.ROW);
        c = Mathf.Clamp(c, 0, GameConstants.COLUMN);

        return (r, c);
    }

    /// <summary>
    /// 월드 좌표 드래그를 서버에 보낼 사각형 범위 [r0, r1) × [c0, c1)로 변환한다.
    /// GetApplesInDraggedArea와 같은 경계 계산을 사용하되 빈칸도 포함하는 영역을 반환한다.
    /// 실제 사과 목록, 영역의 합 또는 선택 성공 여부는 계산하지 않는다.
    /// 출력 순서는 행 시작, 열 시작, 행 끝, 열 끝이다.
    /// </summary>
    public void GetSelectionArea(Vector2 start, Vector2 end, out int r0, out int c0, out int r1, out int c1)
    {
        Vector2 a = transform.InverseTransformPoint(new Vector2(Mathf.Min(start.x, end.x), Mathf.Max(start.y, end.y)));
        Vector2 b = transform.InverseTransformPoint(new Vector2(Mathf.Max(start.x, end.x), Mathf.Min(start.y, end.y)));
        (r0, c0) = GetAppleGridIndex(a);
        (r1, c1) = GetAppleGridIndex(b);
    }

    /// <summary>
    /// 초기 보드 생성 후 서버가 보낸 개인 보드 전체와 비교하여 새로 제거된 칸을 반영한다.
    /// 일반적인 보드 교체 함수가 아니다. 기존 값 유지 또는 비어 있지 않은 칸의 0 전환만 허용한다.
    /// 숫자 변경과 빈칸의 사과 복구는 현재 규칙에 없는 변화이므로 예외로 알린다.
    /// 게임 ID, 요청 번호, 버전 검사는 이 함수 밖의 네트워크 처리 계층에서 담당한다.
    /// </summary>
    public void ApplySnapshot(byte[] state)
    {
        if (state == null || state.Length != appleArray.Length)
            throw new System.ArgumentException("Board size");
        var removed = new List<int>();

        // 비교 단계에서는 실제 보드를 변경하지 않는다.
        // 모든 칸의 전이가 유효한 경우에만 아래에서 제거를 실행한다.
        for (int i = 0; i < state.Length; i++)
        {
            if (state[i] == 0 && appleArray[i] != 0)
                removed.Add(i);
            else if (state[i] != appleArray[i])
                throw new System.InvalidOperationException("Unexpected board replacement");
        }
        RemoveSelectedApples(removed);
    }
}
