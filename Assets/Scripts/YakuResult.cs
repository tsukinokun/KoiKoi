[System.Serializable]
public struct YakuResult
{
    public string Name;
    public int Points;

    public YakuResult(string name, int points)
    {
        Name = name;
        Points = points;
    }
}