using SpeechChem.Tests.NamesFixture;
using Xunit;

namespace SpeechChem.Tests.NamesFixture
{
    // Stand-ins for obfuscated shipping types: "ObfA" plays a renamed type (deob ClassA), ObfB a
    // renamed nested type (deob ClassA/ClassB), Kept a nested type that kept its real name under the
    // renamed parent (SpaceChem's '#=qG74…/EyeColor' = deob 'Class311/EyeColor').
    public class ObfA
    {
        public class ObfB { }

        public class Kept { }

        public int f0;
    }
}

namespace SpeechChem.Tests
{
    public class GameNamesTests
    {
        private const string Ns = "SpeechChem.Tests.NamesFixture.";

        public GameNamesTests()
        {
            GameNames.LoadRows(new[]
            {
                "T\t" + Ns + "ClassA\tObfA",
                "T\t" + Ns + "ClassA/ClassB\tObfB",
                "M\tClass185\tvmethod_3\t#=qTick",
                "F\t" + Ns + "ClassA\tint_0\tf0",
            });
        }

        [Fact]
        public void ForwardLookupsTranslateDeobToShipping()
        {
            Assert.Equal("#=qTick", GameNames.Member("M", "Class185", "vmethod_3"));
            Assert.Equal("f0", GameNames.Member("F", Ns + "ClassA", "int_0"));
        }

        [Fact]
        public void UnmappedMembersKeepTheirName()
        {
            // de4dot leaves some members alone (ctors, name-preserved members): no row, same name.
            Assert.Equal(".ctor", GameNames.Member("M", "Class185", ".ctor"));
        }

        [Fact]
        public void RenamedTypesResolveWithTheirNamespaceReattached()
        {
            // T rows carry only the BARE shipping name; the namespace comes from the deob name.
            Assert.Same(typeof(ObfA), GameNames.Type(typeof(ObfA).Assembly, Ns + "ClassA"));
        }

        [Fact]
        public void ReverseLookupNamesTopLevelAndNestedTypes()
        {
            Assert.Equal(Ns + "ClassA", GameNames.DeobTypeName(typeof(ObfA)));
            Assert.Equal(Ns + "ClassA/ClassB", GameNames.DeobTypeName(typeof(ObfA.ObfB)));
        }

        [Fact]
        public void NestedTypeKeepingItsNameUnderARenamedParentReadsThroughTheParent()
        {
            Assert.Equal(Ns + "ClassA/Kept", GameNames.DeobTypeName(typeof(ObfA.Kept)));
        }

        [Fact]
        public void ReverseMemberLookupFindsTheDeobField()
        {
            Assert.Equal("int_0", GameNames.DeobMemberName("F", Ns + "ClassA", "f0"));
            Assert.Equal("unknown", GameNames.DeobMemberName("F", Ns + "ClassA", "unknown"));
        }

        [Fact]
        public void PrintableMasksObfuscatorCharacters()
        {
            Assert.Equal("#=qA?B", GameNames.Printable("#=qA​B"));
        }
    }
}
