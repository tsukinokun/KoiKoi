using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class HandView : MonoBehaviour
{
    [Header("Layout Settings")]
    [SerializeField] private bool isEnemy = false;    // プレイヤー用ならfalse、NPC用ならtrueにする
    [SerializeField] private float radius = 12.0f;     // 円の半径
    [SerializeField] private float angleStep = 5.0f;   // カード間の角度

    [Header("Animation Settings")]
    [SerializeField] private float moveDuration = 0.4f; // ぬるっと動く時間

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

        // 🌟 修正：初期配布のループ中（8枚未満）なら8枚想定、ゲーム中のドローなら現在の枚数+1で計算
        int anticipatedTotal = (currentCardIndex < 8) ? 8 : (currentCardIndex + 1);

        // 扇形配置の計算
        float baseAngle = isEnemy ? 270.0f : 90.0f;
        float centerOffset = (anticipatedTotal - 1) / 2f;
        float currentAngle = baseAngle + (currentCardIndex - centerOffset) * angleStep * (isEnemy ? 1 : -1);
        float rad = currentAngle * Mathf.Deg2Rad;

        float x = Mathf.Cos(rad) * radius;
        float y = (Mathf.Sin(rad) * radius) + (isEnemy ? radius : -radius);

        Vector3 targetLocalPos = new Vector3(x, y, -0.01f * currentCardIndex);

        Vector3 currentLocalPos = card.transform.localPosition;
        currentLocalPos.z = targetLocalPos.z;
        card.transform.localPosition = currentLocalPos;

        float rotationOffset = isEnemy ? 270.0f : 90.0f;
        card.transform.localRotation = Quaternion.Euler(0, 0, currentAngle - rotationOffset);

        card.MoveToLocalPositionAsync(targetLocalPos, moveDuration, card.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// ゲーム中に札をプレイして「手札が減った時」に、隙間を綺麗に詰めるために呼び出す
    /// </summary>
    // 🌟 修正：除外したいカード（出したカード）を引数で受け取れるようにする（デフォルトはnull）
    public void Rearrange(Card ignoreCard = null)
    {
        // 1️⃣ 実際に残る有効なカードだけをリスト化する
        List<Card> activeCards = new List<Card>();
        foreach (Transform child in this.transform)
        {
            Card card = child.GetComponent<Card>();
            if (card == null) continue;

            // 出したカード、または非アクティブなカードは除外
            if (card == ignoreCard || !card.gameObject.activeSelf) continue;

            activeCards.Add(card);
        }

        int activeCount = activeCards.Count;
        if (activeCount == 0) return;

        // 2️⃣ 厳密に残った枚数（activeCount）を基準に扇形の配置を再計算する
        for (int index = 0; index < activeCount; index++)
        {
            Card card = activeCards[index];

            float baseAngle = isEnemy ? 270.0f : 90.0f;

            // 🌟 childCount ではなく、確定した残り枚数（activeCount）を使う
            float centerOffset = (activeCount - 1) / 2f;
            float currentAngle = baseAngle + (index - centerOffset) * angleStep * (isEnemy ? 1 : -1);
            float rad = currentAngle * Mathf.Deg2Rad;

            float x = Mathf.Cos(rad) * radius;
            float y = (Mathf.Sin(rad) * radius) + (isEnemy ? radius : -radius);

            // 🌟 Z軸（重ね順）もインデックスに応じて再設定
            Vector3 targetLocalPos = new Vector3(x, y, -0.01f * index);

            // 角度（回転）も残り枚数の位置に合わせてスムーズに補間する
            float rotationOffset = isEnemy ? 270.0f : 90.0f;
            card.transform.localRotation = Quaternion.Euler(0, 0, currentAngle - rotationOffset);

            // 減った枚数に合わせてキュッと詰める移動
            card.MoveToLocalPositionAsync(targetLocalPos, moveDuration, card.GetCancellationTokenOnDestroy()).Forget();
        }
    }
}