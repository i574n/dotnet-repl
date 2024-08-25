﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Documents;
using KernelInfo = Microsoft.DotNet.Interactive.Documents.KernelInfo;

namespace dotnet_repl;

public static class DocumentParser
{
    public static async Task<InteractiveDocument> LoadInteractiveDocumentAsync(
        FileInfo file,
        CompositeKernel kernel)
    {
        var kernelInfos = CreateKernelInfos(kernel);
        return await InteractiveDocument.LoadAsync(file, kernelInfos);
    }

    public static KernelInfoCollection CreateKernelInfos(this CompositeKernel kernel)
    {
        KernelInfoCollection kernelInfos = new();

        var kernelChoosers = kernel.KernelInfo.SupportedDirectives;

        foreach (var kernelChooser in kernelChoosers)
        {
            kernelInfos.Add(new KernelInfo(kernelChooser.Name[2..]));
        }

        return kernelInfos;
    }
}