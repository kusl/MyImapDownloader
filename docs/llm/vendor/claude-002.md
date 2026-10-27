how do I search when I only know a portion of the sender's email address not the full sender email address? for example, lets say an email comes from something at claude dot ai 
`time myemailsearch search from:claude` gives me nothing 

```bash
$ time myemailsearch search from:claude
info: MyEmailSearch.Search.SearchEngine[0]
      Executing search: from:claude
info: MyEmailSearch.Search.SearchEngine[0]
      Search completed: 0 results returned, 0 total matches in 13ms
No results found.

real	0m0.145s
user	0m0.114s
sys	0m0.023s
$ time myemailsearch search from:*@claude.ai
info: MyEmailSearch.Search.SearchEngine[0]
      Executing search: from:*@claude.ai
info: MyEmailSearch.Search.SearchEngine[0]
      Search completed: 0 results returned, 0 total matches in 14539ms
No results found.

real	0m14.666s
user	0m0.385s
sys	0m2.365s
```





Claude
/
how do I search when I only know a portion of the sender's email address not the full sender email address? for example, lets say an email comes from something at claude dot ai 
time myemailsearch search from:claude gives me nothing 

$ time myemailsearch search from:claude
info: MyEmailSearch.Search.SearchEngine[0]
      Executing search: from:claude
info: MyEmailSearch.Search.SearchEngine[0]
      Search completed: 0 results returned, 0 total matches in 13ms
No results found.

real    0m0.145s
user    0m0.114s
sys    0m0.023s
$ time myemailsearch search from:*@claude.ai
info: MyEmailSearch.Search.SearchEngine[0]
      Executing search: from:*@claude.ai
info: MyEmailSearch.Search.SearchEngine[0]
      Search completed: 0 results returned, 0 total matches in 14539ms
No results found.

real    0m14.666s
user    0m0.385s
sys    0m2.365s
Ran 4 commands
Checking which email address pattern matches the sender field.
The short answer: use * on both sides, and quote it so the shell leaves it alone.

bash
myemailsearch search 'from:*claude*'
Here is why your two attempts behaved the way they did, based on AddQueryConditions in SearchDatabase.cs (dump.txt around line 6721).

from:claude returned nothing because from: without a * is an exact match. The code generates emails.from_address = @fromAddress, so claude only matches a sender whose entire address is literally claude. This is different from to: and subject:, which always wrap the value in %...% and do substring matching. The inconsistency is easy to trip over.

from:*@claude.ai ran correctly but matched nothing. The * is converted to SQL %, so the query became from_address LIKE '%@claude.ai'. That means the address has to end in exactly @claude.ai. If the mail actually comes from a subdomain such as something@mail.claude.ai, or from another domain such as anthropic.com, the match fails. *claude* catches all of those.

The 14.5 seconds is a full table scan. A pattern that starts with % can’t use idx_emails_from, so SQLite reads every row. It does this twice, once for the results and once for GetTotalCountAsync. Every leading-wildcard sender search will be about that slow on your archive. A prefix pattern like from:noreply* can be faster, but SQLite only uses the index for LIKE prefixes under specific collation and pragma conditions, so I wouldn’t count on it.

If 'from:*claude*' still finds nothing, check what is actually stored in the index:

bash
sqlite3 /path/to/your/search.db \
  "SELECT from_address, COUNT(*) FROM emails
   WHERE from_address LIKE '%claude%' OR from_address LIKE '%anthropic%'
   GROUP BY 1 ORDER BY 2 DESC LIMIT 20;"
That will show the real sender addresses.

Two code changes would fix this properly:

Make from: without a * do a substring match (%value%), the same as to:.
Add a from_address trigram FTS5 index so substring sender searches are fast instead of scanning 35 GB of metadata.
The first is a few lines plus test updates. The second needs a schema migration and a reindex. I can write either one as a complete delivery.




Claude is AI and can make mistakes. Please double-check responses.






Claude finished the response
