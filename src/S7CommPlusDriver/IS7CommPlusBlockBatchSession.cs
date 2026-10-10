using System.Collections.Generic;

namespace S7CommPlusDriver
{
    internal interface IS7CommPlusBlockBatchSession
    {
        int GetBlockContents(IReadOnlyList<uint> relationIds, bool sourceOnly, out List<S7CommPlusClientBlockContent> contents);
    }
}
