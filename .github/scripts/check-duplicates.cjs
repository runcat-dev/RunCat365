module.exports = async ({ github, context }) => {
  const issue = context.payload.issue;
  const owner = context.repo.owner;
  const repo = context.repo.repo;
  const title = issue.title;

  // Clean special characters but keep the natural phrasing
  const cleanTitle = title
    .replace(/[^\w\s]/gi, ' ')
    .replace(/\s+/g, ' ')
    .trim();

  const words = cleanTitle.split(/\s+/);

  if (cleanTitle.length < 10 || words.length < 3) {
    console.log("Title is too short or lacks context to search for duplicates. Skipping.");
    return;
  }

  const query = `repo:${owner}/${repo} in:title ${cleanTitle} -number:${issue.number}`;
  console.log(`Searching with hybrid query: "${query}"`);

  try {
    const response = await github.rest.search.issuesAndPullRequests({
      q: query,
      search_type: 'hybrid',
      per_page: 5
    });

    const items = response.data.items;

    if (items && items.length > 0) {
      let commentBody = `### 🔍 Potential Duplicate Issues or PRs Found\n\n`;
      commentBody += `Hello @${issue.user.login},\n\n`;
      commentBody += `We found some existing issues or Pull Requests that might cover a similar topic. Please review them to see if they already address your concern:\n\n`;

      for (const item of items) {
        const type = item.pull_request ? 'Pull Request' : 'Issue';
        const state = item.state === 'open' ? '🟢 Open' : '🔴 Closed';
        commentBody += `- [${type} #${item.number}](${item.html_url}) - *${item.title}* (${state})\n`;
      }

      commentBody += `\n---\n`;
      commentBody += `*This is an automated check. Maintainers will review this issue and decide whether to close it as a duplicate or keep it open.*`;

      await github.rest.issues.createComment({
        owner: owner,
        repo: repo,
        issue_number: issue.number,
        body: commentBody
      });

      await github.rest.issues.addLabels({
        owner: owner,
        repo: repo,
        issue_number: issue.number,
        labels: ['potential-duplicate']
      });

      console.log(`Found ${items.length} potential duplicates and commented.`);
    } else {
      console.log("No potential duplicates found.");
    }
  } catch (error) {
    console.error("Error searching for duplicates:", error);
  }
};
