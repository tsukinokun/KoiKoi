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
    [SerializeField] private float xSpacing = 0.3f; // 横にずらす幅
    [SerializeField] private Vector3 cardScale = new Vector3(0.5f, 0.5f, 1f); // 獲得札の大きさ

    [Tooltip("親の中心から、どれくらい左側から並べ始めるか（マイナス値で左へ）")]
    [SerializeField] private float xStartOffset = -1.5f; // 🌟ここをインスペクターで調整して左端に合わせます

    /// <summary>
    /// カードをタイプに応じて適切な親トランスフォームに割り振り、綺麗に整列させる
    /// </summary>
    public void AddCard(Card card, string cardType)
    {
        if (card == null) return;

        string typeKey = cardType.ToLower();
        Transform targetParent = null;

        if (typeKey == "hikari") targetParent = hikariParent;
        else if (typeKey == "tane") targetParent = taneParent;
        else if (typeKey == "tan" || typeKey == "tanzaku") targetParent = tanParent;
        else targetParent = kasuParent;

        if (targetParent == null)
        {
            Debug.LogWarning($"獲得エリアの親トランスフォームが未設定です。タイプ: {cardType}");
            targetParent = this.transform;
        }

        card.transform.SetParent(targetParent);

        // サイズ調整
        card.transform.localScale = cardScale;

        // 獲得エリア内での整列計算
        int childCount = targetParent.childCount;

        // 🌟 xStartOffset（初期の左ズレ分）を足すことで、親の位置が真ん中でも左端から並びます
        float xPosition = xStartOffset + ((childCount - 1) * xSpacing);

        card.transform.localPosition = new Vector3(xPosition, 0, -0.01f * childCount);
        card.transform.localRotation = Quaternion.identity;
    }
}