namespace PedeAqui.Exception;

public static class ResourceErrorMessages
{
    public const string INVALID_REQUEST = "Um ou mais campos da requisição são inválidos.";
    public const string UNKNOWN_ERROR = "Ocorreu um erro inesperado. Tente novamente mais tarde.";

    public const string NAME_REQUIRED = "O nome é obrigatório.";
    public const string EMAIL_REQUIRED = "O e-mail é obrigatório.";
    public const string EMAIL_INVALID = "O e-mail informado é inválido.";
    public const string EMAIL_ALREADY_REGISTERED = "Já existe um usuário cadastrado com este e-mail.";
    public const string EMAIL_OR_PASSWORD_INVALID = "E-mail ou senha inválidos.";
    public const string PASSWORD_REQUIRED = "A senha é obrigatória.";
    public const string PASSWORD_MIN_LENGTH = "A senha deve ter no mínimo 8 caracteres.";
    public const string PASSWORD_UPPERCASE = "A senha deve conter ao menos uma letra maiúscula.";
    public const string PASSWORD_LOWERCASE = "A senha deve conter ao menos uma letra minúscula.";
    public const string PASSWORD_NUMBER = "A senha deve conter ao menos um número.";
    public const string PASSWORD_SPECIAL_CHARACTER = "A senha deve conter ao menos um caractere especial.";

    public const string PRODUCT_NOT_FOUND = "Produto não encontrado.";
    public const string PRODUCT_NAME_REQUIRED = "O nome do produto é obrigatório.";
    public const string PRODUCT_NAME_MAX_LENGTH = "O nome do produto deve ter no máximo 150 caracteres.";
    public const string PRODUCT_SKU_REQUIRED = "O SKU do produto é obrigatório.";
    public const string PRODUCT_SKU_MAX_LENGTH = "O SKU deve ter no máximo 50 caracteres.";
    public const string PRODUCT_SKU_ALREADY_EXISTS = "Já existe um produto com este SKU.";
    public const string PRODUCT_PRICE_MUST_BE_GREATER_THAN_ZERO = "O preço do produto deve ser maior que zero.";
    public const string PRODUCT_INACTIVE = "O produto está inativo e não pode ser vendido.";

    public const string STOCK_QUANTITY_MUST_BE_GREATER_THAN_ZERO = "A quantidade deve ser maior que zero.";
    public const string STOCK_REASON_REQUIRED = "O motivo da movimentação é obrigatório.";
    public const string STOCK_REASON_MAX_LENGTH = "O motivo deve ter no máximo 200 caracteres.";
    public const string INSUFFICIENT_STOCK = "Estoque insuficiente.";

    public const string ORDER_NOT_FOUND = "Pedido não encontrado.";
    public const string ORDER_MUST_HAVE_ITEMS = "O pedido deve ter ao menos um item.";
    public const string ORDER_ITEM_PRODUCT_REQUIRED = "Todo item deve informar o produto.";
    public const string ORDER_ITEM_QUANTITY_MUST_BE_GREATER_THAN_ZERO = "A quantidade de cada item deve ser maior que zero.";
    public const string ORDER_DUPLICATED_PRODUCT = "O mesmo produto não pode aparecer em mais de um item do pedido.";
    public const string ORDER_CANNOT_BE_CHANGED = "Somente pedidos pendentes podem ser alterados.";
    public const string ORDER_CANNOT_BE_CANCELLED = "Somente pedidos pendentes podem ser cancelados.";
    public const string ORDER_CANNOT_BE_CONFIRMED = "Somente pedidos pendentes podem ser confirmados.";
}
