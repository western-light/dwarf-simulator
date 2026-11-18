using UnityEngine;

public class FPD_HeaderAttribute : PropertyAttribute
{
    public string HeaderText;
    public float UpperPadding;
    public float BottomPadding;
    public float Height;
    public Color BGColor;

    public FPD_HeaderAttribute(string headerText, float upperPadding = 6f, float bottomPadding = 4f, int addHeight = 2)
    {
        HeaderText = headerText;
        UpperPadding = upperPadding;
        BottomPadding = bottomPadding;
        Height = addHeight;
        BGColor = Color.white;
    }

    public FPD_HeaderAttribute( string headerText, float r, float g, float b, float upperPadding, float bottomPadding, int addHeight = 2 ) : this( headerText, upperPadding, bottomPadding, addHeight )
    {
        BGColor = new Color( r, g, b, 1f );
    }
}

