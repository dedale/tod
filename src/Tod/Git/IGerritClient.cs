using Tod.Git;

namespace Tod.Gerrit;

internal interface IGerritClient
{
    Task<bool> IsKnown(Sha1 commit);
}
