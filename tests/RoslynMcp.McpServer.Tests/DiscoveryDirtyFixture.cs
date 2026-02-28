using System;

namespace Sample;

public class DiscoveryDirtyFixture
{
    private int _value;

    public void Mixed(int input, bool flag)
    {
        int unused;

        if (flag == true)
        {
            _value = input + 0;
            Console.WriteLine(_value);
        }
    }

    public int Compute()
    {
        return _value;
    }
}
