namespace SharpPy
{
    public class Program
    {
        public static void Main()
        {
            Console.WriteLine("Hello World!");

            Demo.CompletePythonSystemDemo.Demo();
            Demo.VMIntegrationDemo.Demo();
            var test = new SharpPy.Verification.Python312SyntaxVerification();
            test.VerifyAllSyntax();

            return;
        }
    }
}
