using System;
using System.Collections.Generic;

public static class LocalBoardRules
{
    public static byte[] Generate(Random random)
    {
        var b=new byte[GameConstants.ROW*GameConstants.COLUMN];int sum=0;
        for(int i=0;i<b.Length;i++){b[i]=(byte)random.Next(1,10);sum+=b[i];}
        if(sum%10==0)return b;
        int at=random.Next(b.Length);int target=(10-(sum-b[at])%10)%10;
        if(target!=0){b[at]=(byte)target;return b;}
        int second=random.Next(b.Length);if(second==at)second=(second+1)%b.Length;
        int rest=sum-b[at]-b[second];var pairs=new List<(int,int)>();
        for(int a=1;a<=9;a++)for(int c=1;c<=9;c++)if((rest+a+c)%10==0)pairs.Add((a,c));
        var pair=pairs[random.Next(pairs.Count)];b[at]=(byte)pair.Item1;b[second]=(byte)pair.Item2;
        return b;
    }
}
