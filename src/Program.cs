using WasmEmitter;

static class Program
{
    static void Main()
    {
        var tree = new TreeEmitter();
        var printI = tree.Add(new ImportFunction("PrintI", new VoidType(), [new(new IntType(), "i")], "console.log(i);"));
        var main = tree.Add(new ExportFunction("Main", new IntType(), []));
        var i = new Local(new IntType());
        main.Statement = new Block([
            new Assign(i, new Int(4)),
            new Call(printI, [i]),
            new Increment(i),
            new Return(i)
        ]);

        var html = tree.Emit(true);
        File.WriteAllText("index.html", html);
    }
}