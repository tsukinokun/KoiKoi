using UnityEngine;

/// <summary>
/// 獲得した札をタイプ（光・種・短冊・カス）ごとに仕分け、整列させるビューコンポーネント
/// </summary>
public class CapturedAreaView : MonoBehaviour
{
    [Header("Captured Category Parents")]
    [SerializeField] private Transform hikariParent;
    [SerializeField] private Transform taneParent;
    [SerializeField] private Transform tanParent;
    [SerializeField] private Transform kasuParent;

    [Header("Layout Settings")]
    [SerializeField] private float xSpacing = 0.2f; // 横にずらす幅

    /// <summary>
    /// カードをタイプに応じて適切な親トランスフォームに割り振り、綺麗に整列させる
    /// </summary>
    public void AddCard(Card card, string cardType)
    {
        if (card == null) return;

        // 文字列のブレを考慮して小文字化
        string typeKey = cardType.ToLower();
        Transform targetParent = null;

        // 適切な親を決定
        if (typeKey == "hikari") targetParent = hikariParent;
        else if (typeKey == "tane") targetParent = taneParent;
        else if (typeKey == "tan" || typeKey == "tanzaku") targetParent = tanParent;
        else targetParent = kasuParent;

        if (targetParent == null)
        {
            Debug.LogWarning($"獲得エリアの親トランスフォームが未設定です。タイプ: {cardType}");
            targetParent = this.transform; // 最悪のケースは自身の直下に入れる
        }

        // 親を付け替える
        card.transform.SetParent(targetParent);

        // 獲得エリア内での整列計算（GameManagerから引っ越してきたロジック）
        int childCount = targetParent.childCount;

        // Z軸を僅かに手前に出すことで、重なりの描画順（ソート）を正しく保つ
        card.transform.localPosition = new Vector3((childCount - 1) * xSpacing, 0, -0.01f * childCount);
        card.transform.localRotation = Quaternion.identity;
    }
}