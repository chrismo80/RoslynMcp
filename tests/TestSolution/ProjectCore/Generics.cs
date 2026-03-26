namespace ProjectCore;

public abstract class GenericWorker<TItem>
{
    public abstract void Work(TItem item);
    
    public abstract void Finish<TType>();
}

public class MainWorker : GenericWorker<string>
{
    public override void Work(string value)
    {
        value = "";
    }

    public override void Finish<TType>()
    {
        throw new NotImplementedException();
    }
}

public class SubWorker : GenericWorker<int>
{
    public override void Work(int value)
    {
        value = 0;
    }

    public override void Finish<TType>()
    {
        throw new NotImplementedException();
    }
}