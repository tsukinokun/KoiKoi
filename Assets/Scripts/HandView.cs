using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class HandView : MonoBehaviour
{
    [Header("Layout Settings")]
    [SerializeField] private bool isEnemy = false;    // プレイヤー用ならfalse、NPC用ならtrueにする
    [SerializeField] private float radius = 12.0f;     // 円の半径
    [SerializeField] private float angleStep = 5.0f;   // カード間の角度
    [SerializeField] private int initialHandCount = 8; // 初期配布時に想定する手札枚数（扇の中心を決めるため）

    [Header("Animation Settings")]
    [SerializeField] private float moveDuration = 0.2f; // ぬるっと動く時間

    /// <summary>
    /// このビューが持つカードのうちデータを保持しているものを列挙する
    /// </summary>
    public IEnumerable<Card> Cards => transform.Cast<Transform>()
        .Select(t => t.GetComponent<Card>())
        .Where(c => c != null && c.Data != null);

    /// <summary>
    /// GameManager側（DistributeHandCard）からは今まで通りこの形で呼ばれる
    /// </summary>
    public void AddCard(Card card, bool isFaceUp)
    {
        if (card == null) return;

        // 子要素にする前の「現在の枚数」が、このカードのインデックス（0番目〜）
        int currentCardIndex = this.transform.childCount;

        card.transform.SetParent(this.transform, worldPositionStays: false);
        card.SetFaceUp(isFaceUp);
        card.transform.localPosition = Vector3.zero;

        // 初期配布のループ中（initialHandCount枚未満）ならinitialHandCount枚想定、ゲーム中のドローなら現在の枚数+1で計算
        int anticipatedTotal = (currentCardIndex < initialHandCount) ? initialHandCount : (currentCardIndex + 1);

        (Vector3 targetLocalPos, Quaternion targetRotation) = CalculateSlot(currentCardIndex, anticipatedTotal);

        Vector3 currentLocalPos = card.transform.localPosition;
        currentLocalPos.z = targetLocalPos.z;
        card.transform.localPosition = currentLocalPos;
        card.transform.localRotation = targetRotation;

        card.MoveToLocalPositionAsync(targetLocalPos, moveDuration, card.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// ゲーム中に札をプレイして「手札が減った時」に、隙間を綺麗に詰めるために呼び出す
    /// </summary>
    // 🌟 除外したいカード（出したカード）を引数で受け取れるようにする（デフォルトはnull）
    public void Rearrange(Card ignoreCard = null)
    {
        // 1️⃣ 実際に残る有効なカードだけをリスト化する
        List<Card> activeCards = transform.Cast<Transform>()
            .Select(t => t.GetComponent<Card>())
            .Where(card => card != null && card != ignoreCard && card.gameObject.activeSelf)
            .ToList();

        int activeCount = activeCards.Count;
        if (activeCount == 0) return;

        // 2️⃣ 厳密に残った枚数（activeCount）を基準に扇形の配置を再計算する
        for (int index = 0; index < activeCount; index++)
        {
            Card card = activeCards[index];

            (Vector3 targetLocalPos, Quaternion targetRotation) = CalculateSlot(index, activeCount);

            card.transform.localRotation = targetRotation;

            // 減った枚数に合わせてキュッと詰める移動
            card.MoveToLocalPositionAsync(targetLocalPos, moveDuration, card.GetCancellationTokenOnDestroy()).Forget();
        }
    }

    /// <summary>
    /// 扇形配置における、指定インデックス（全体枚数total中のindex番目）のローカル座標と回転を計算する
    /// </summary>
    private (Vector3 localPosition, Quaternion rotation) CalculateSlot(int index, int total)
    {
        float baseAngle = isEnemy ? 270.0f : 90.0f;
        float centerOffset = (total - 1) / 2f;
        float currentAngle = baseAngle + (index - centerOffset) * angleStep * (isEnemy ? 1 : -1);
        float rad = currentAngle * Mathf.Deg2Rad;

        float x = Mathf.Cos(rad) * radius;
        float y = (Mathf.Sin(rad) * radius) + (isEnemy ? radius : -radius);

        Vector3 localPosition = new Vector3(x, y, -0.01f * index);

        float rotationOffset = isEnemy ? 270.0f : 90.0f;
        Quaternion rotation = Quaternion.Euler(0, 0, currentAngle - rotationOffset);

        return (localPosition, rotation);
    }
}
