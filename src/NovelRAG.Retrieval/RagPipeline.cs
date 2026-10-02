using NovelRAG.Application;
using NovelRAG.Domain;
using System.Text.RegularExpressions;

namespace NovelRAG.Retrieval;

public sealed class RagPipeline(IRetriever retriever, ILLMProvider llm, IAppLog? log = null) : IChatPipeline
{
    public async Task<ChatAnswer> AskAsync(RetrievalQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Text)) throw new ArgumentException("请输入要查找的问题。");
        var found = await retriever.RetrieveAsync(query with { Limit = 6, Offset = 0 }, ct);
        var context = found.Results.Select(r => r with { Excerpt = r.Excerpt.Length > 1000 ? r.Excerpt[..1000] : r.Excerpt }).ToArray();
        if (context.Length == 0) return new("没有找到足够的灵感记录来回答。可尝试更具体的关键词，或检查语义索引。" + (found.Warning is null ? "" : "\n" + found.Warning), [], true);
        try
        {
            var response = await llm.AnswerAsync(query.Text, context, ct);
            if (string.IsNullOrWhiteSpace(response.Answer) || response.SourceIds.Count == 0 || response.SourceIds.Any(id => context.All(r => r.Idea.Id != id)))
                throw new InvalidDataException("模型未提供有效来源。");
            foreach (Match citation in Regex.Matches(response.Answer, @"\[#(\d+)\]"))
                if (!long.TryParse(citation.Groups[1].Value, out var id) || !response.SourceIds.Contains(id))
                    throw new InvalidDataException("回答中包含未验证的引用。");
            var sources = context.Where(r => response.SourceIds.Contains(r.Idea.Id)).ToArray();
            return new(response.Answer + "\n\n请结合下方原始记录核对回答。" + (found.Warning is null ? "" : "\n" + found.Warning), sources, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log?.Write("rag.fallback", ex);
            return new("本机模型不可用或回答缺少有效引用。以下是检索到的原始片段，尚不能据此确认问题中的推断：\n\n" +
                string.Join("\n\n", context.Select(r => $"[#{r.Idea.Id}] {r.Excerpt}")), context, true);
        }
    }
}
