export interface ChatCitation {
  file: string;
  startLine: number;
  endLine: number;
  score: number;
}

export interface ChatMessageDto {
  role: 'user' | 'assistant';
  content: string;
}

export interface ChatRequest {
  question: string;
  history?: ChatMessageDto[];
}

export interface ChatResponse {
  content: string;
  citations: ChatCitation[];
  grounded: boolean;
}

export interface ChatTurn {
  role: 'user' | 'assistant';
  content: string;
  citations?: ChatCitation[] | null;
}
