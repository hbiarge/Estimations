using Acheve.Common.Shared;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public class IdempotencyTests
    {
        [Fact]
        public void The_same_client_and_key_name_the_same_case()
        {
            Idempotency.CaseNumberFor("client", "key-1").ShouldBe(Idempotency.CaseNumberFor("client", "key-1"));
        }

        [Fact]
        public void Another_key_or_client_names_another_case()
        {
            var case1 = Idempotency.CaseNumberFor("client", "key-1");

            Idempotency.CaseNumberFor("client", "key-2").ShouldNotBe(case1);
            Idempotency.CaseNumberFor("other", "key-1").ShouldNotBe(case1);
        }

        [Fact]
        public void Moving_characters_between_client_and_key_changes_the_case()
        {
            Idempotency.CaseNumberFor("ab", "c").ShouldNotBe(Idempotency.CaseNumberFor("a", "bc"));
        }
    }
}
